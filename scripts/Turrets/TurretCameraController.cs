#nullable enable

using Godot;
using Game.Interfaces;
using Game.Projectiles;
using Game.Components;
using Game.Components.Nodes;
using Game.Singletons;

namespace Game.Turrets;

/// <summary>
/// Контроллер камеры для турели. 
/// Рассчитывает точку прицеливания (raycast из камеры) и передает углы в ControllableTurret.
/// </summary>
public partial class TurretCameraController : Node, ICameraController
{
    public enum AimingMode
    {
        /// <summary>
        /// Турель просто копирует поворот камеры (как в шутерах от первого лица).
        /// </summary>
        RotationBased,

        /// <summary>
        /// Камера указывает точку в мире, турель сводится на эту точку (как в World of Tanks).
        /// </summary>
        ConvergedTarget
    }

    #region Components & Settings

    private CameraOperator? _cameraOperator;

    [ExportGroup("Components")]
    [Export] private Camera3D? _camera;
    [Export] private Shaker3D? _shaker;

    [ExportGroup("Aiming Properties")]
    [Export] public AimingMode Mode { get; private set; } = AimingMode.ConvergedTarget;

    private float _sensitivityMultiplier = 1.0f;
    private float _defaultFov;

    [Export(PropertyHint.Range, "0.1, 5.0, 0.1")]
    public float SensitivityMultiplier
    {
        get => _sensitivityMultiplier;
        set
        {
            _sensitivityMultiplier = value;
            if (_cameraOperator != null) _cameraOperator.NodeSensitivity = value;
        }
    }

    [Export(PropertyHint.Range, "100, 5000, 100")]
    private float _aimTargetDistance = 2000f;

    [ExportGroup("Limits")]
    [ExportSubgroup("Turret Limits")]
    [Export] public float TurretMinPitch { get; private set; } = -20f;
    [Export] public float TurretMaxPitch { get; private set; } = 45f;
    [Export] public float TurretMaxYaw { get; private set; } = 90f;

    [ExportSubgroup("Camera Limits")]
    [Export] public float CameraMinPitch { get; private set; } = -89f;
    [Export] public float CameraMaxPitch { get; private set; } = 89f;
    [Export] public float CameraMaxYaw { get; private set; } = -1f; // -1 = без ограничений

    #endregion

    private PlayerControllableTurret? _ownerTurret;
    private PhysicsRayQueryParameters3D _rayQuery = null!;

    // Кэшированные значения в радианах
    private float _turretMinPitchRad, _turretMaxPitchRad, _turretMaxYawRad;
    private bool _isInitialized = false;

    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    /// <summary>
    /// Инициализация контроллера при посадке в турель.
    /// </summary>
    public void Initialize(PlayerControllableTurret owner)
    {
        if (_isInitialized) return;
        _ownerTurret = owner;

        _cameraOperator = new CameraOperator();

#if DEBUG
        if (_camera == null) GD.PushError($"[{Name}] Camera3D is missing.");
        if (_shaker == null) GD.PushError($"[{Name}] Shaker3D is missing.");
#endif

        // Настройка Raycast (исключаем снаряды)
        uint aimCollisionMask = GetCollisionMaskFromProjectileScene();
        _rayQuery = new PhysicsRayQueryParameters3D { CollisionMask = aimCollisionMask };

        // Кэшируем радианы для оптимизации
        _turretMinPitchRad = Mathf.DegToRad(TurretMinPitch);
        _turretMaxPitchRad = Mathf.DegToRad(TurretMaxPitch);
        _turretMaxYawRad = Mathf.DegToRad(TurretMaxYaw);

        if (_cameraOperator != null && _camera != null)
        {
            _cameraOperator.NodeSensitivity = SensitivityMultiplier;
            _cameraOperator.MinPitch = CameraMinPitch;
            _cameraOperator.MaxPitch = CameraMaxPitch;
            _cameraOperator.MaxYaw = CameraMaxYaw;

            // Связываем оператор с камерой (тряска и поворот применяются к ней)
            _cameraOperator.Initialize(_camera, _camera, _shaker);

            _defaultFov = _camera.Fov;
        }

        _isInitialized = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ownerTurret == null) return;

        (float targetYaw, float targetPitch) = CalculateAimAngles();
        _ownerTurret.SetAimTarget(targetYaw, targetPitch);
    }

    #region Aiming Logic

    private (float, float) CalculateAimAngles()
    {
        return Mode switch
        {
            AimingMode.RotationBased => CalculateRotationBasedAngles(),
            AimingMode.ConvergedTarget => CalculateConvergedAngles(),
            _ => (_ownerTurret!.Rotation.Y, _ownerTurret!.Rotation.X)
        };
    }

    private (float, float) CalculateRotationBasedAngles()
    {
        // В этом режиме турель пытается выровняться параллельно камере
        Basis targetGlobalBasis = _camera!.GlobalTransform.Basis;
        Basis localBasis = _ownerTurret!.GlobalTransform.Basis.Inverse() * targetGlobalBasis;
        Vector3 targetEuler = localBasis.GetEuler();

        float targetYaw = _turretMaxYawRad < 0
            ? targetEuler.Y
            : Mathf.Clamp(targetEuler.Y, -_turretMaxYawRad, _turretMaxYawRad);

        float targetPitch = Mathf.Clamp(targetEuler.X, _turretMinPitchRad, _turretMaxPitchRad);

        return (targetYaw, targetPitch);
    }

    private (float, float) CalculateConvergedAngles()
    {
        var camTransform = _camera!.GlobalTransform;

        // 1. Пускаем луч из камеры вперед
        _rayQuery.From = camTransform.Origin;
        // Basis.Z смотрит назад, поэтому для "вперед" вычитаем Z
        _rayQuery.To = _rayQuery.From - camTransform.Basis.Z * _aimTargetDistance;

        var result = World.DirectSpaceState.IntersectRay(_rayQuery);
        Vector3 targetPoint = result.Count > 0 ? (Vector3)result["position"] : _rayQuery.To;

        // 2. Находим вектор от турели к найденной точке
        Vector3 aimVectorGlobal = targetPoint - _ownerTurret!.GlobalPosition;

        // 3. Переводим вектор в локальную систему координат турели.
        // Это критически важно: если танк стоит под наклоном, локальные оси тоже наклонены.
        // Inverse() * Vector позволяет получить координаты вектора относительно базы.
        Vector3 localAimVector = _ownerTurret.GlobalTransform.Basis.Inverse() * aimVectorGlobal;

        // 4. Вычисляем углы Yaw и Pitch
        // Atan2(x, z) дает угол на плоскости XZ. -X и -Z используются для коррекции Forward в Godot (-Z).
        float targetYawRad = Mathf.Atan2(-localAimVector.X, -localAimVector.Z);

        // Расстояние в горизонтальной плоскости (для расчета подъема ствола)
        float horizontalDist = new Vector2(localAimVector.X, localAimVector.Z).Length();
        float targetPitchRad = Mathf.Atan2(localAimVector.Y, horizontalDist);

        // 5. Ограничиваем углы (Clamp)
        if (_turretMaxYawRad >= 0)
        {
            targetYawRad = Mathf.Clamp(targetYawRad, -_turretMaxYawRad, _turretMaxYawRad);
        }
        targetPitchRad = Mathf.Clamp(targetPitchRad, _turretMinPitchRad, _turretMaxPitchRad);

        return (targetYawRad, targetPitchRad);
    }

    /// <summary>
    /// Получает маску коллизий из префаба снаряда, чтобы камера "видела" сквозь то же, что и снаряд.
    /// </summary>
    private uint GetCollisionMaskFromProjectileScene()
    {
        if (_ownerTurret!.ProjectileScene == null)
        {
            GD.PushWarning($"[{Name}] В турели '{_ownerTurret.Name}' не назначен снаряд.");
            return 1;
        }

        if (_ownerTurret.ProjectileScene.Instantiate() is BaseProjectile projectileInstance)
        {
            uint mask = projectileInstance.CollisionMask;
            projectileInstance.QueueFree(); // Сразу удаляем
            return mask;
        }

        GD.PushError($"[{Name}] Сцена снаряда должна наследовать BaseProjectile.");
        return 1;
    }

    #endregion

    #region ICameraController Implementation

    public void Activate()
    {
        if (!_isInitialized)
        {
            GD.PushError($"[{Name}] Попытка активации без инициализации!");
            return;
        }

        // Добавляем саму турель и игрока в исключения рейкаста (чтобы не целиться в свою антенну)
        if (_rayQuery.Exclude.Count == 0 && _ownerTurret?.PlayerController != null)
        {
            _rayQuery.Exclude =
            [
                _ownerTurret.GetRid(),
                _ownerTurret.PlayerController.GetRid()
            ];
        }

        SetPhysicsProcess(true);
        _ownerTurret?.SetPhysicsProcess(true);
        if (_camera != null) _camera.Current = true;
    }

    public void Deactivate()
    {
        SetPhysicsProcess(false);
        _ownerTurret?.SetPhysicsProcess(false);
        if (_camera != null) _camera.Current = false;
    }

    public void HandleMouseInput(Vector2 mouseDelta, float delta)
    {
        _cameraOperator?.Update(mouseDelta, delta);
    }

    public void HandleMouseInput(Vector2 mouseDelta)
    {
        HandleMouseInput(mouseDelta, (float)GetProcessDeltaTime());
    }

    public Camera3D GetCamera() => _camera!;

    public IOwnerCameraController GetCameraOwner() => _ownerTurret!;

    public void ApplyShake(float duration, float strength)
    {
        _shaker?.StartShake(duration, strength, true);
    }

    public void SetSensitivityModifier(float modifier)
    {
        if (_cameraOperator != null)
        {
            _cameraOperator.DynamicSensitivity = modifier;
        }
    }

    /// <summary>
    /// Регулирует чувствительность мыши в зависимости от текущего угла обзора (FOV).
    /// Используется при зумировании: чем меньше FOV (ближе зум), тем ниже чувствительность.
    /// </summary>
    /// <param name="currentFov">Текущий угол обзора камеры в градусах.</param>
    public void AdjustSensitivityByFov(float currentFov)
    {
        if (_camera == null || Mathf.IsEqualApprox(_defaultFov, 0f)) return;

        // Формула: Отношение текущего FOV к базовому.
        // Если FOV упал с 75 до 25 (зум 3x), то ratio будет 0.33.
        // Чувствительность снизится до 33% от базовой.
        float ratio = currentFov / _defaultFov;

        // Можно добавить Clamp, чтобы не уйти в 0 или бесконечность при ошибках
        ratio = Mathf.Clamp(ratio, 0.01f, 1.0f);

        // Используем уже существующий метод интерфейса ICameraController
        SetSensitivityModifier(ratio);
    }

    /// <summary>
    /// Альтернативный метод: Регулировка по уровню приближения (Zoom Factor).
    /// </summary>
    /// <param name="zoomLevel">Кратность зума (например, 1.0f - норма, 2.0f - x2, 4.0f - x4).</param>
    public void AdjustSensitivityByZoomLevel(float zoomLevel)
    {
        if (zoomLevel <= 0.001f) return;

        // Если зум x4, то чувствительность должна быть 1/4 (0.25)
        float modifier = 1.0f / zoomLevel;

        SetSensitivityModifier(modifier);
    }

    #endregion
}

#nullable disable