using Godot;
using Game.Turrets;

namespace Game.UI;

/// <summary>
/// Управляет шейдерным прицелом турели с эффектом сжатия при выстреле.
/// </summary>
public partial class TurretReticle : Control
{
    [ExportGroup("Shader")]
    [Export] private ColorRect _reticleRect;

    [ExportGroup("Layout")]
    [Export] public float ReticleGap { get; set; } = 45f;
    [Export] public float DiamondBaseSize { get; set; } = 12f;
    [Export] public float EdgeMargin { get; set; } = 40f;
    [Export] public float PixelsPerDegree { get; set; } = 12f;

    [ExportGroup("Spread Settings")]
    [Export] public float SpreadIdle { get; set; } = 0f;
    [Export] public float SpreadShooting { get; set; } = -8f;      // Отрицательный = сжатие!
    [Export] public float SpreadCooldown { get; set; } = 5f;
    [Export] public float SpreadReloading { get; set; } = 25f;
    [Export] public float SpreadNoAmmo { get; set; } = 15f;
    [Export] public float SpreadBroken { get; set; } = 40f;

    [ExportGroup("Dynamics")]
    [Export] public float ExpansionSpeed { get; set; } = 12f;
    [Export] public float SqueezeSpeed { get; set; } = 25f;        // Быстрое сжатие
    [Export] public float RecoilImpulse { get; set; } = 30f;       // После выстрела
    [Export] public float RecoilDecay { get; set; } = 6f;
    [Export] public float RotationSpeed { get; set; } = 4f;

    private PlayerControllableTurret _turret;
    private TurretCameraController _cameraController;
    private ShaderMaterial _shaderMaterial;

    // Состояние анимации
    private float _currentSpread = 50f;
    private float _targetSpread = 0f;
    private float _recoilOffset = 0f;
    private float _squeezeOffset = 0f;         // Сжатие при выстреле
    private float _diamondRotation = 0f;
    private float _targetDiamondRotation = 0f;
    private float _stateTime = 0f;
    private int _currentShaderState = 0;
    private bool _isShooting = false;

    // Дальномер
    private float _targetDistanceDisplay = 0f;

    public event System.Action<float> OnAimingIntensityChanged;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        if (_reticleRect != null)
        {
            _shaderMaterial = _reticleRect.Material as ShaderMaterial;
            _reticleRect.SetAnchorsPreset(LayoutPreset.FullRect);
            _reticleRect.MouseFilter = MouseFilterEnum.Ignore;
        }
    }

    public void Initialize(PlayerControllableTurret turret, TurretCameraController camController)
    {
        // Отписка от старой турели
        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
        }

        _turret = turret;
        _cameraController = camController;
        _currentSpread = 50f;
        _recoilOffset = 0f;
        _squeezeOffset = 0f;
        _stateTime = 0f;
        _isShooting = false;

        if (_turret != null)
        {
            _turret.OnStateChanged += OnTurretStateChanged;
            _turret.OnShot += OnTurretShot;
            UpdateTargetState();
        }

        UpdateShaderParams();
        _reticleRect.Visible = true;
    }

    public override void _ExitTree()
    {
        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
        }
    }

    private void OnTurretStateChanged(ShootingTurret.TurretState newState)
    {
        _stateTime = 0f;

        // При начале выстрела - запускаем сжатие
        if (newState == ShootingTurret.TurretState.Shooting)
        {
            _isShooting = true;
            _squeezeOffset = SpreadShooting; // Мгновенное сжатие
        }
        else
        {
            _isShooting = false;
        }

        UpdateTargetState();
    }

    private void OnTurretShot()
    {
        // Когда реально произошёл выстрел - отдача
        _recoilOffset = RecoilImpulse;
        _squeezeOffset = 0f; // Сбрасываем сжатие, теперь работает отдача
    }

    private void UpdateTargetState()
    {
        if (_turret == null) return;

        // Приоритет состояний
        if (!_turret.IsAlive || _turret.CurrentState == ShootingTurret.TurretState.Broken)
        {
            _targetSpread = SpreadBroken;
            _currentShaderState = 4; // broken
        }
        else if (_turret.CurrentState == ShootingTurret.TurretState.Shooting)
        {
            _targetSpread = SpreadShooting; // Сжатие!
            _currentShaderState = 5; // shooting (новое)
        }
        else if (_turret.CurrentState == ShootingTurret.TurretState.Reloading)
        {
            _targetSpread = SpreadReloading;
            _currentShaderState = 2; // reloading
        }
        else if (!_turret.HasAmmoInMag && !_turret.HasInfiniteAmmo)
        {
            _targetSpread = SpreadNoAmmo;
            _currentShaderState = 3; // no_ammo
        }
        else if (_turret.CurrentState == ShootingTurret.TurretState.FiringCooldown)
        {
            _targetSpread = SpreadCooldown;
            _currentShaderState = 1; // cooldown
        }
        else
        {
            _targetSpread = SpreadIdle;
            _currentShaderState = 0; // idle
        }

        // Интенсивность для сетки
        float maxSpread = Mathf.Max(SpreadBroken, SpreadReloading);
        float aimingIntensity = 1.0f - Mathf.Clamp(_targetSpread / maxSpread, 0f, 1f);
        OnAimingIntensityChanged?.Invoke(aimingIntensity);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_turret == null || !Visible) return;
        float dt = (float)delta;

        _stateTime += dt;

        // Вращение ромба при перезарядке
        if (_turret.CurrentState == ShootingTurret.TurretState.Reloading)
        {
            _targetDiamondRotation += dt * RotationSpeed;
        }
        else
        {
            float snapAngle = Mathf.Pi / 2f;
            float nearestSnap = Mathf.Round(_targetDiamondRotation / snapAngle) * snapAngle;
            _targetDiamondRotation = Mathf.Lerp(_targetDiamondRotation, nearestSnap, dt * 3f);
        }
        _diamondRotation = Mathf.Lerp(_diamondRotation, _targetDiamondRotation, dt * 8f);

        // Сжатие при выстреле (быстрее чем обычное движение)
        if (_isShooting)
        {
            _squeezeOffset = Mathf.Lerp(_squeezeOffset, SpreadShooting, dt * SqueezeSpeed);
        }
        else
        {
            _squeezeOffset = Mathf.Lerp(_squeezeOffset, 0f, dt * ExpansionSpeed);
        }

        // Отдача (работает после выстрела)
        _recoilOffset = Mathf.Lerp(_recoilOffset, 0f, dt * RecoilDecay);

        // Итоговый spread = базовый + сжатие + отдача
        float targetTotal = _targetSpread + _squeezeOffset + _recoilOffset;

        // Скорость зависит от направления
        float speed = targetTotal < _currentSpread ? SqueezeSpeed : ExpansionSpeed;
        _currentSpread = Mathf.Lerp(_currentSpread, targetTotal, dt * speed);

        // Дальномер
        float realDist = GetTargetDistance();
        _targetDistanceDisplay = Mathf.Lerp(_targetDistanceDisplay, realDist, dt * 8f);

        UpdateShaderParams();
    }

    private void UpdateShaderParams()
    {
        if (_shaderMaterial == null || _turret == null) return;

        float currentYawDeg = Mathf.RadToDeg(_turret.TurretYaw.Rotation.Y);
        float currentPitchDeg = Mathf.RadToDeg(_turret.TurretPitch.Rotation.X);

        _shaderMaterial.SetShaderParameter("spread", _currentSpread);
        _shaderMaterial.SetShaderParameter("diamond_rotation", _diamondRotation);
        _shaderMaterial.SetShaderParameter("yaw_degrees", currentYawDeg);
        _shaderMaterial.SetShaderParameter("pitch_degrees", currentPitchDeg);
        _shaderMaterial.SetShaderParameter("turret_state", _currentShaderState);
        _shaderMaterial.SetShaderParameter("state_time", _stateTime);
        _shaderMaterial.SetShaderParameter("reticle_gap", ReticleGap);
        _shaderMaterial.SetShaderParameter("diamond_base_size", DiamondBaseSize);
        _shaderMaterial.SetShaderParameter("edge_margin", EdgeMargin);
        _shaderMaterial.SetShaderParameter("pixels_per_degree", PixelsPerDegree);
    }

    // Для совместимости с TurretHUD
    public void OnShoot()
    {
        // Теперь основная логика в OnTurretShot через событие
    }

    public float GetDisplayDistance() => _targetDistanceDisplay;

    private float GetTargetDistance()
    {
        var cam = _cameraController?.GetCamera();
        if (cam == null) return 0f;

        var spaceState = cam.GetWorld3D().DirectSpaceState;
        var from = cam.GlobalPosition;
        var to = from - cam.GlobalTransform.Basis.Z * 3000f;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        if (_turret != null) query.Exclude = [_turret.GetRid()];

        var result = spaceState.IntersectRay(query);
        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            return from.DistanceTo(hitPos);
        }

        return 0f;
    }
}