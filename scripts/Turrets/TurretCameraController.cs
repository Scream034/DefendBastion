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
        /// Ствол турели жестко следует за вращением камеры. Просто и предсказуемо.
        /// </summary>
        RotationBased,
        /// <summary>
        /// Ствол турели наводится на точку в мире, куда смотрит камера.
        /// Учитывает смещение камеры относительно ствола (коррекция параллакса) для точного прицеливания.
        /// </summary>
        ConvergedTarget
    }

    [ExportGroup("Required Nodes")]
    [Export] private Camera3D _camera;
    [Export] private Node3D _turretYaw;
    [Export] private Node3D _turretPitch;
    [Export] private Node3D _barrelEnd;

    [ExportGroup("Aiming Properties")]
    [Export] public AimingMode Mode { get; private set; } = AimingMode.ConvergedTarget;
    [Export(PropertyHint.Range, "0.1, 5.0, 0.1")] public float SensitivityMultiplier { get; private set; } = 1.0f;
    [Export(PropertyHint.Range, "1, 20, 0.1")] private float _aimSpeed = 8f;
    [Export(PropertyHint.Range, "100, 5000, 100")] private float _aimTargetDistance = 2000f;

    [ExportGroup("Rotation Limits (Degrees)")]
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch { get; private set; } = -20f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch { get; private set; } = 45f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw { get; private set; } = 90f;

    private uint _aimCollisionMask; // Кэшированная маска коллизий для прицеливания
    private PhysicsRayQueryParameters3D _rayQuery; // Переиспользуемый объект запроса

    // Кэшированные значения в радианах
    private float _minPitchRad, _maxPitchRad, _maxYawRad;
    private ControllableTurret _ownerTurret;
    private Vector2 _cameraRotation; // Внутреннее состояние вращения камеры

    // Тряска камеры
    private FastNoiseLite _shakeNoise = new();
    private float _shakeStrength = 0f;
    private ulong _shakeSeed;

    public override void _Ready()
    {
        _ownerTurret = GetOwner<ControllableTurret>();
        if (_ownerTurret == null || _barrelEnd == null)
        {
            GD.PushError("TurretCameraController должен быть дочерним узлом ControllableTurret и иметь ссылку на BarrelEnd!");
            SetProcess(false);
            return;
        }

        // Получаем маску коллизий из сцены снаряда, которую использует турель
        _aimCollisionMask = GetCollisionMaskFromProjectileScene();

        // Создаем объект запроса один раз для переиспользования
        _rayQuery = new PhysicsRayQueryParameters3D
        {
            CollisionMask = _aimCollisionMask
        };

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
        switch (Mode)
        {
            case AimingMode.RotationBased:
                ProcessRotationBasedAim(delta);
                break;
            case AimingMode.ConvergedTarget:
                ProcessConvergedAim(delta); // <-- Вызываем новый, исправленный метод
                break;
        }
    }

    private void ProcessRotationBasedAim(double delta)
    {
        Basis targetGlobalBasis = _camera.GlobalTransform.Basis;
        Basis localBasis = _ownerTurret.GlobalTransform.Basis.Inverse() * targetGlobalBasis;
        Vector3 targetEuler = localBasis.GetEuler();

        float targetYaw = _maxYawRad < 0 ? targetEuler.Y : Mathf.Clamp(targetEuler.Y, -_maxYawRad, _maxYawRad);
        float targetPitch = Mathf.Clamp(targetEuler.X, _minPitchRad, _maxPitchRad);

        float fDelta = (float)delta;
        _turretYaw.Rotation = _turretYaw.Rotation with { Y = Mathf.LerpAngle(_turretYaw.Rotation.Y, targetYaw, _aimSpeed * fDelta) };
        _turretPitch.Rotation = _turretPitch.Rotation with { X = Mathf.LerpAngle(_turretPitch.Rotation.X, targetPitch, _aimSpeed * fDelta) };
    }

    private void ProcessConvergedAim(double delta)
    {
        // Находим целевую точку в мире (куда смотрит камера)
        var camTransform = _camera.GlobalTransform;
        _rayQuery.From = camTransform.Origin;
        _rayQuery.To = _rayQuery.From - camTransform.Basis.Z * _aimTargetDistance;
        var result = Constants.DirectSpaceState.IntersectRay(_rayQuery);
        Vector3 targetPoint = result.Count > 0 ? (Vector3)result["position"] : _rayQuery.To;

        // Определяем вектор, по которому должен лететь снаряд.
        // Он идет ОТ КОНЦА СТВОЛА к ЦЕЛИ. Это ключ к решению проблемы параллакса.
        Vector3 aimVector = targetPoint - _barrelEnd.GlobalPosition;

        // Преобразуем этот глобальный вектор направления в локальные углы для турели.
        // Yaw (горизонталь) вычисляется относительно неподвижной базы турели.
        Vector3 localAimVector = _ownerTurret.GlobalTransform.Basis.Inverse() * aimVector;
        float targetYawRad = Mathf.Atan2(-localAimVector.X, -localAimVector.Z);

        // Pitch (вертикаль) вычисляется как угол между горизонтальной плоскостью и вектором.
        var horizontalDist = new Vector2(localAimVector.X, localAimVector.Z).Length();
        float targetPitchRad = Mathf.Atan2(localAimVector.Y, horizontalDist);

        // Ограничиваем вычисленные углы
        if (_maxYawRad >= 0)
        {
            targetYawRad = Mathf.Clamp(targetYawRad, -_maxYawRad, _maxYawRad);
        }
        targetPitchRad = Mathf.Clamp(targetPitchRad, _minPitchRad, _maxPitchRad);

        // Плавно интерполируем текущие углы к целевым
        float fDelta = (float)delta;
        _turretYaw.Rotation = _turretYaw.Rotation with { Y = Mathf.LerpAngle(_turretYaw.Rotation.Y, targetYawRad, _aimSpeed * fDelta) };
        _turretPitch.Rotation = _turretPitch.Rotation with { X = Mathf.LerpAngle(_turretPitch.Rotation.X, targetPitchRad, _aimSpeed * fDelta) };
    }

    /// <summary>
    /// Вспомогательный метод для безопасного получения маски коллизий из сцены снаряда.
    /// </summary>
    private uint GetCollisionMaskFromProjectileScene()
    {
        var projectileScene = _ownerTurret.GetProjectileScene();
        if (projectileScene == null)
        {
            GD.PushWarning($"Турель '{_ownerTurret.Name}' не имеет сцены снаряда. Прицеливание LookAtTarget может работать некорректно.");
            return 1; // Возвращаем маску по умолчанию (слой 1)
        }

        // Создаем временный экземпляр, чтобы прочитать его свойство
        if (projectileScene.Instantiate() is BaseProjectile projectileInstance)
        {
            uint mask = projectileInstance.CollisionMask;
            // Сразу же удаляем временный объект
            projectileInstance.QueueFree();
            return mask;
        }

        GD.PushError($"Сцена '{projectileScene.ResourcePath}' не содержит узла, наследуемого от BaseProjectile.");
        return 1;
    }

    #region ICameraController Implementation

    public void Activate()
    {
        SetProcess(true);
        SetPhysicsProcess(true);
        _camera.Current = true;
    }

    public void Deactivate()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        _camera.Current = false;
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