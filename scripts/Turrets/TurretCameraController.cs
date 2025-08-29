#nullable enable

using Godot;
using Game.Interfaces;
using Game.Projectiles;
using Game.Singletons;
using Game.Components;
using Game.Components.Nodes;

namespace Game.Turrets;

public partial class TurretCameraController : Node, ICameraController
{
    public enum AimingMode { RotationBased, ConvergedTarget }

    private CameraOperator? _cameraOperator;

    [ExportGroup("Components")]
    [Export] private Camera3D? _camera;
    [Export] private Shaker3D? _shaker;

    [ExportGroup("Aiming Properties")]
    [Export] public AimingMode Mode { get; private set; } = AimingMode.ConvergedTarget;
    [Export(PropertyHint.Range, "0.1, 5.0, 0.1")] public float SensitivityMultiplier { get; private set; } = 0.05f;
    [Export(PropertyHint.Range, "100, 5000, 100")] private float _aimTargetDistance = 2000f;

    [ExportGroup("Turret Rotation Limits (Degrees)", "Turret")]
    [Export(PropertyHint.Range, "-90, 0, 1")] public float TurretMinPitch { get; private set; } = -20f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float TurretMaxPitch { get; private set; } = 45f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float TurretMaxYaw { get; private set; } = 90f;

    [ExportGroup("Camera Rotation Limits (Degrees)", "Camera")]
    [Export(PropertyHint.Range, "-90, 0, 1")] public float CameraMinPitch { get; private set; } = -89f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float CameraMaxPitch { get; private set; } = 89f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float CameraMaxYaw { get; private set; } = -1f;

    private PlayerControllableTurret? _ownerTurret;
    private PhysicsRayQueryParameters3D _rayQuery = null!;
    private float _turretMinPitchRad, _turretMaxPitchRad, _turretMaxYawRad;
    private bool _isInitialized = false;

    public void Initialize(PlayerControllableTurret owner)
    {
        if (_isInitialized) return;
        _ownerTurret = owner;

        _cameraOperator = new();

#if DEBUG
        if (_camera == null) GD.PushError("Для TurretCameraController не назначена Camera3D.");
        if (_shaker == null) GD.PushError("Для TurretCameraController не назначен Shaker3D.");
#endif

        uint aimCollisionMask = GetCollisionMaskFromProjectileScene();
        _rayQuery = new() { CollisionMask = aimCollisionMask };

        _turretMinPitchRad = Mathf.DegToRad(TurretMinPitch);
        _turretMaxPitchRad = Mathf.DegToRad(TurretMaxPitch);
        _turretMaxYawRad = Mathf.DegToRad(TurretMaxYaw);

        // Инициализируем оператора
        if (_cameraOperator != null && _camera != null)
        {
            _cameraOperator.SensitivityMultiplier = SensitivityMultiplier;
            _cameraOperator.MinPitch = CameraMinPitch;
            _cameraOperator.MaxPitch = CameraMaxPitch;
            _cameraOperator.MaxYaw = CameraMaxYaw;
            // Здесь и вращаем, и двигаем один и тот же узел - саму камеру
            _cameraOperator.Initialize(_camera, _camera, _shaker);
        }

        _isInitialized = true;
    }

    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        (float targetYaw, float targetPitch) = CalculateAimAngles();
        _ownerTurret?.SetAimTarget(targetYaw, targetPitch);
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
        Basis targetGlobalBasis = _camera!.GlobalTransform.Basis;
        Basis localBasis = _ownerTurret!.GlobalTransform.Basis.Inverse() * targetGlobalBasis;
        Vector3 targetEuler = localBasis.GetEuler();

        float targetYaw = _turretMaxYawRad < 0 ? targetEuler.Y : Mathf.Clamp(targetEuler.Y, -_turretMaxYawRad, _turretMaxYawRad);
        float targetPitch = Mathf.Clamp(targetEuler.X, _turretMinPitchRad, _turretMaxPitchRad);

        return (targetYaw, targetPitch);
    }

    private (float, float) CalculateConvergedAngles()
    {
        var camTransform = _camera!.GlobalTransform;
        _rayQuery.From = camTransform.Origin;
        _rayQuery.To = _rayQuery.From - camTransform.Basis.Z * _aimTargetDistance;
        var result = Constants.DirectSpaceState.IntersectRay(_rayQuery);
        Vector3 targetPoint = result.Count > 0 ? (Vector3)result["position"] : _rayQuery.To;

        Vector3 aimVector = targetPoint - _ownerTurret!.BarrelEnd.GlobalPosition;

        Vector3 localAimVector = _ownerTurret.GlobalTransform.Basis.Inverse() * aimVector;
        float targetYawRad = Mathf.Atan2(-localAimVector.X, -localAimVector.Z);

        var horizontalDist = new Vector2(localAimVector.X, localAimVector.Z).Length();
        float targetPitchRad = Mathf.Atan2(localAimVector.Y, horizontalDist);

        if (_turretMaxYawRad >= 0)
        {
            targetYawRad = Mathf.Clamp(targetYawRad, -_turretMaxYawRad, _turretMaxYawRad);
        }
        targetPitchRad = Mathf.Clamp(targetPitchRad, _turretMinPitchRad, _turretMaxPitchRad);

        return (targetYawRad, targetPitchRad);
    }
    
    private uint GetCollisionMaskFromProjectileScene()
    {
        if (_ownerTurret!.ProjectileScene == null)
        {
            GD.PushWarning($"Турель '{_ownerTurret.Name}' не имеет сцены снаряда. Прицеливание LookAtTarget может работать некорректно.");
            return 1;
        }

        if (_ownerTurret.ProjectileScene.Instantiate() is BaseProjectile projectileInstance)
        {
            uint mask = projectileInstance.CollisionMask;
            projectileInstance.QueueFree();
            return mask;
        }

        GD.PushError($"Сцена '{_ownerTurret.ProjectileScene.ResourcePath}' не содержит узла, наследуемого от BaseProjectile.");
        return 1;
    }
    
    #endregion

    #region ICameraController Implementation

    public void Activate()
    {
        if (!_isInitialized)
        {
            GD.PushError("TurretCameraController не был инициализирован перед активацией!");
            return;
        }

        if (_rayQuery.Exclude.Count == 0 && _ownerTurret?.PlayerController != null)
        {
            _rayQuery.Exclude = [_ownerTurret.GetRid(), _ownerTurret.PlayerController.GetRid()];
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
        // Делегируем всю работу оператору
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

    #endregion
}
#nullable disable