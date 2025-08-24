using Godot;
using Game.Interfaces;
using Game.Player;
using System.Threading.Tasks;
using Game.Projectiles;
using Game.Singletons;

namespace Game.Turrets;

/// <summary>
/// Компонент, отвечающий за управление камерой и прицеливанием турели.
/// Реализует ICameraController, позволяя PlayerInputManager передавать ему управление.
/// Поддерживает два режима прицеливания: прямое вращение и наведение на цель.
/// </summary>
public partial class TurretCameraController : Node, ICameraController
{
    /// <summary>
    /// Определяет, как турель будет наводиться на цель.
    /// </summary>
    public enum AimingMode
    {
        /// <summary>
        /// Углы турели жестко следуют за вращением камеры.
        /// </summary>
        RotationBased,
        /// <summary>
        /// Углы турели вычисляются для наведения на точку, куда смотрит камера,
        /// с учетом коррекции параллакса.
        /// </summary>
        ConvergedTarget
    }

    [ExportGroup("Components")]
    [Export] private Camera3D _camera;

    [ExportGroup("Aiming Properties")]
    [Export] public AimingMode Mode { get; private set; } = AimingMode.ConvergedTarget;
    [Export(PropertyHint.Range, "0.1, 5.0, 0.1")] public float SensitivityMultiplier { get; private set; } = 1.0f;
    [Export(PropertyHint.Range, "100, 5000, 100")] private float _aimTargetDistance = 2000f;

    [ExportGroup("Rotation Limits (Degrees)")]
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch { get; private set; } = -20f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch { get; private set; } = 45f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw { get; private set; } = 90f;

    // --- Кэшированные ссылки и параметры ---
    private PlayerControllableTurret _ownerTurret;
    private PhysicsRayQueryParameters3D _rayQuery;
    private float _minPitchRad, _maxPitchRad, _maxYawRad;
    private Vector2 _cameraRotation;

    // --- Параметры для эффектов ---
    private FastNoiseLite _shakeNoise = new();
    private float _shakeStrength = 0f;
    private ulong _shakeSeed;

    public override void _Ready()
    {
        // Кэшируем все необходимые ссылки и параметры один раз для оптимизации
        _ownerTurret = GetOwner<PlayerControllableTurret>();
#if DEBUG
        if (_ownerTurret == null)
        {
            GD.PushError("TurretCameraController должен быть дочерним узлом ControllableTurret!");
            SetProcess(false);
            return;
        }
#endif

        uint aimCollisionMask = GetCollisionMaskFromProjectileScene();
        _rayQuery = new() { CollisionMask = aimCollisionMask };

        _minPitchRad = Mathf.DegToRad(MinPitch);
        _maxPitchRad = Mathf.DegToRad(MaxPitch);
        _maxYawRad = Mathf.DegToRad(MaxYaw);

        _shakeNoise.Seed = (int)GD.Randi();
        _shakeNoise.Frequency = 0.5f;

        SetProcess(false);
        SetPhysicsProcess(false);
    }

    public override void _Process(double delta)
    {
        // Применяем вращение к камере и эффекты
        _camera.Rotation = new Vector3(_cameraRotation.X, _cameraRotation.Y, 0);
        if (_shakeStrength > 0)
        {
            _shakeSeed++;
            var amount = _shakeNoise.GetNoise2D(_shakeSeed, _shakeSeed) * _shakeStrength;
            _camera.Rotation += new Vector3(amount, amount, amount);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Вычисляем целевые углы и передаем их турели
        (float targetYaw, float targetPitch) = CalculateAimAngles();
        _ownerTurret.SetAimTarget(targetYaw, targetPitch);
    }

    /// <summary>
    /// Главный метод, вычисляющий целевые углы в зависимости от режима.
    /// </summary>
    /// <returns>Кортеж с целевыми углами (Yaw, Pitch) в радианах.</returns>
    private (float, float) CalculateAimAngles()
    {
        return Mode switch
        {
            AimingMode.RotationBased => CalculateRotationBasedAngles(),
            AimingMode.ConvergedTarget => CalculateConvergedAngles(),
            _ => (_ownerTurret.Rotation.Y, _ownerTurret.Rotation.X)
        };
    }

    private (float, float) CalculateRotationBasedAngles()
    {
        Basis targetGlobalBasis = _camera.GlobalTransform.Basis;
        Basis localBasis = _ownerTurret.GlobalTransform.Basis.Inverse() * targetGlobalBasis;
        Vector3 targetEuler = localBasis.GetEuler();

        float targetYaw = _maxYawRad < 0 ? targetEuler.Y : Mathf.Clamp(targetEuler.Y, -_maxYawRad, _maxYawRad);
        float targetPitch = Mathf.Clamp(targetEuler.X, _minPitchRad, _maxPitchRad);

        return (targetYaw, targetPitch);
    }

    private (float, float) CalculateConvergedAngles()
    {
        var camTransform = _camera.GlobalTransform;
        _rayQuery.From = camTransform.Origin;
        _rayQuery.To = _rayQuery.From - camTransform.Basis.Z * _aimTargetDistance;
        var result = Constants.DirectSpaceState.IntersectRay(_rayQuery);
        Vector3 targetPoint = result.Count > 0 ? (Vector3)result["position"] : _rayQuery.To;

        Vector3 aimVector = targetPoint - _ownerTurret.BarrelEnd.GlobalPosition;

        Vector3 localAimVector = _ownerTurret.GlobalTransform.Basis.Inverse() * aimVector;
        float targetYawRad = Mathf.Atan2(-localAimVector.X, -localAimVector.Z);

        var horizontalDist = new Vector2(localAimVector.X, localAimVector.Z).Length();
        float targetPitchRad = Mathf.Atan2(localAimVector.Y, horizontalDist);

        if (_maxYawRad >= 0)
        {
            targetYawRad = Mathf.Clamp(targetYawRad, -_maxYawRad, _maxYawRad);
        }
        targetPitchRad = Mathf.Clamp(targetPitchRad, _minPitchRad, _maxPitchRad);

        return (targetYawRad, targetPitchRad);
    }

    /// <summary>
    /// Вспомогательный метод для безопасного получения маски коллизий из сцены снаряда.
    /// </summary>
    private uint GetCollisionMaskFromProjectileScene()
    {
#if DEBUG
        if (_ownerTurret.ProjectileScene == null)
        {
            GD.PushWarning($"Турель '{_ownerTurret.Name}' не имеет сцены снаряда. Прицеливание LookAtTarget может работать некорректно.");
            return 1; // Возвращаем маску по умолчанию (слой 1)
        }
#endif

        // Создаем временный экземпляр, чтобы прочитать его свойство
        if (_ownerTurret.ProjectileScene.Instantiate() is BaseProjectile projectileInstance)
        {
            uint mask = projectileInstance.CollisionMask;
            // Сразу же удаляем временный объект
            projectileInstance.QueueFree();
            return mask;
        }

        GD.PushError($"Сцена '{_ownerTurret.ProjectileScene.ResourcePath}' не содержит узла, наследуемого от BaseProjectile.");
        return 1;
    }

    #region ICameraController Implementation

    public void Activate()
    {
        // Если нет исключений для турели и игрока, то добавляем их.
        if (_rayQuery.Exclude.Count == 0)
        {
            _rayQuery.Exclude = [_ownerTurret.GetRid(), _ownerTurret.PlayerController.GetRid()];
        }

        SetProcess(true);
        SetPhysicsProcess(true);
        _ownerTurret.SetPhysicsProcess(true);
        _camera.Current = true;
#if DEBUG
        GD.Print($"[{this}-{Name}] Была активирована!");
#endif
    }

    public void Deactivate()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        _ownerTurret.SetPhysicsProcess(false);
        _camera.Current = false;

#if DEBUG
        GD.Print($"[{this}:{Name}] Была деактивирована!");
#endif
    }

    public void HandleMouseInput(Vector2 mouseDelta)
    {
        // Вращаем наше внутреннее состояние камеры. _Process применит его к реальной камере.
        _cameraRotation.Y -= mouseDelta.X * CameraController.MouseSensitivityMultiplier * SensitivityMultiplier;
        _cameraRotation.X -= mouseDelta.Y * CameraController.MouseSensitivityMultiplier * SensitivityMultiplier;

        // Ограничиваем вращение самой камеры, чтобы она не "вылетала" за пределы вида
        _cameraRotation.X = Mathf.Clamp(_cameraRotation.X, Mathf.DegToRad(-89), Mathf.DegToRad(89));
    }

    public Camera3D GetCamera() => _camera;

    public IOwnerCameraController GetCameraOwner() => (IOwnerCameraController)_ownerTurret;

    #endregion

    public async void ShakeAsync(float duration, float strength)
    {
        _shakeStrength = strength;
        await Task.Delay((int)(duration * 1000));
        _shakeStrength = 0f;
    }
}