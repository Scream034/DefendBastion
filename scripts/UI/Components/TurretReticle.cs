#nullable enable

using Godot;
using Game.Turrets;
using Game.UI.HUD;

namespace Game.UI.Components;

/// <summary>
/// Управляет шейдерным прицелом турели.
/// Кинематографичная анимация выстрела: долгое схождение → резкое сжатие → вспышка.
/// </summary>
public partial class TurretReticle : Control
{
    [ExportGroup("Shader")]
    [Export] private ColorRect _reticleRect = null!;

    [ExportGroup("Layout")]
    [Export] public float ReticleGap { get; set; } = 45f;
    [Export] public float DiamondBaseSize { get; set; } = 12f;
    [Export] public float EdgeMargin { get; set; } = 40f;
    [Export] public float PixelsPerDegree { get; set; } = 12f;

    [ExportGroup("Spread Settings")]
    [Export] public float SpreadIdle { get; set; } = 0f;
    [Export] public float SpreadShooting { get; set; } = -1.0f;
    [Export] public float SpreadCooldown { get; set; } = 5f;
    [Export] public float SpreadReloading { get; set; } = 25f;
    [Export] public float SpreadNoAmmo { get; set; } = 15f;
    [Export] public float SpreadBroken { get; set; } = 40f;

    [ExportGroup("Dynamics")]
    [Export] public float ExpansionSpeed { get; set; } = 10f;
    [Export] public float SqueezeSpeed { get; set; } = 40f;
    [Export] public float RecoilImpulse { get; set; } = 40f;
    [Export] public float RecoilDecay { get; set; } = 5f;
    [Export] public float RotationSpeed { get; set; } = 4f;

    [ExportGroup("Shot Animation Phases")]
    [Export] public float ConvergenceHoldTime { get; set; } = 0.25f;     // Как долго линии сходятся
    [Export] public float ConvergenceSpeed { get; set; } = 3.0f;         // Скорость схождения линий
    [Export] public float SqueezeDelay { get; set; } = 0.15f;            // Задержка перед резким сжатием
    [Export] public float FlashIntensity { get; set; } = 1.2f;           // Яркость вспышки
    [Export] public float FlashDecay { get; set; } = 15f;                // Скорость угасания вспышки
    [Export] public float GlitchIntensity { get; set; } = 0.3f;

    [ExportGroup("Cooling Effects")]
    [Export] public float FrostIntensity { get; set; } = 0.8f;

    private PlayerControllableTurret? _turret;
    private TurretCameraController? _cameraController;
    private ShaderMaterial? _shaderMaterial;

    // Состояние анимации
    private float _currentSpread = 50f;
    private float _targetSpread = 0f;
    private float _recoilOffset = 0f;
    private float _squeezeOffset = 0f;
    private float _diamondRotation = 0f;
    private float _targetDiamondRotation = 0f;
    private float _stateTime = 0f;
    private int _currentShaderState = 0;

    // Фазы выстрела
    private bool _isInShootingSequence = false;
    private float _shootSequenceTime = 0f;
    private bool _hasSqueezeTriggered = false;

    // Эффекты
    private float _convergenceIntensity = 0f;
    private float _convergenceTarget = 0f;
    private float _shotFlash = 0f;
    private float _frostLevel = 0f;
    private float _impactRing = 0f;  // Кольцо удара

    // Дальномер
    private float _targetDistanceDisplay = 0f;

    public event System.Action<float>? OnAimingIntensityChanged;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        if (_reticleRect != null)
        {
            _shaderMaterial = _reticleRect.Material as ShaderMaterial;
            _reticleRect.SetAnchorsPreset(LayoutPreset.FullRect);
            _reticleRect.MouseFilter = MouseFilterEnum.Ignore;
        }

        Deinitialize();
    }

    public void Initialize(PlayerControllableTurret turret, TurretCameraController camController)
    {
        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
        }

        _turret = turret;
        _cameraController = camController;

        // Сброс состояния
        _currentSpread = 50f;
        _recoilOffset = 0f;
        _squeezeOffset = 0f;
        _convergenceIntensity = 0f;
        _convergenceTarget = 0f;
        _shotFlash = 0f;
        _frostLevel = 0f;
        _impactRing = 0f;
        _stateTime = 0f;
        _isInShootingSequence = false;
        _shootSequenceTime = 0f;
        _hasSqueezeTriggered = false;

        if (_turret != null)
        {
            _turret.OnStateChanged += OnTurretStateChanged;
            _turret.OnShot += OnTurretShot;
            UpdateTargetState();
        }

        UpdateShaderParams();
        _reticleRect.Visible = true;
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    public void Deinitialize()
    {
        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
            _turret = null;
        }

        _reticleRect.Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
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

        if (newState == ShootingTurret.TurretState.Shooting)
        {
            // Начало последовательности выстрела
            StartShootingSequence();
        }
        else
        {
            _isInShootingSequence = false;
        }

        UpdateTargetState();
    }

    /// <summary>
    /// Запуск кинематографичной последовательности выстрела.
    /// </summary>
    private void StartShootingSequence()
    {
        _isInShootingSequence = true;
        _shootSequenceTime = 0f;
        _hasSqueezeTriggered = false;

        // Фаза 1: Линии начинают сходиться (медленно)
        _convergenceTarget = 1.0f;
        _convergenceIntensity = 0f;
    }

    private void OnTurretShot()
    {
        // Момент выстрела! Резкое завершение.

        // Мгновенное сжатие
        _squeezeOffset = SpreadShooting * 1.5f;  // Еще глубже!
        _hasSqueezeTriggered = true;

        // Яркая вспышка
        _shotFlash = FlashIntensity;

        // Кольцо удара
        _impactRing = 1.0f;

        // Схождение завершено - линии "схлопнулись"
        _convergenceIntensity = 0f;
        _convergenceTarget = 0f;

        // Отдача (после небольшой задержки)
        GetTree().CreateTimer(0.03f).Timeout += () =>
        {
            _recoilOffset = RecoilImpulse;
        };

        // Глитч
        SharedHUD.TriggerColoredGlitch(
            new Color(0.3f, 0.9f, 0.85f, 1f),  // Циановый оттенок
            GlitchIntensity,
            0.12f
        );
    }

    private void UpdateTargetState()
    {
        if (_turret == null) return;

        if (!_turret.IsAlive || _turret.CurrentState == ShootingTurret.TurretState.Broken)
        {
            _targetSpread = SpreadBroken;
            _currentShaderState = 4;
        }
        else if (_turret.CurrentState == ShootingTurret.TurretState.Shooting)
        {
            _targetSpread = SpreadShooting;
            _currentShaderState = 5;
        }
        else if (_turret.CurrentState == ShootingTurret.TurretState.Reloading)
        {
            _targetSpread = SpreadReloading;
            _currentShaderState = 2;
        }
        else if (!_turret.HasAmmoInMag && !_turret.HasInfiniteAmmo)
        {
            _targetSpread = SpreadNoAmmo;
            _currentShaderState = 3;
        }
        else if (_turret.CurrentState == ShootingTurret.TurretState.FiringCooldown)
        {
            _targetSpread = SpreadCooldown;
            _currentShaderState = 1;
        }
        else
        {
            _targetSpread = SpreadIdle;
            _currentShaderState = 0;
        }

        float maxSpread = Mathf.Max(SpreadBroken, SpreadReloading);
        float aimingIntensity = 1.0f - Mathf.Clamp(_targetSpread / maxSpread, 0f, 1f);
        OnAimingIntensityChanged?.Invoke(aimingIntensity);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_turret == null || !Visible) return;
        float dt = (float)delta;

        _stateTime += dt;

        // === ОБРАБОТКА ПОСЛЕДОВАТЕЛЬНОСТИ ВЫСТРЕЛА ===
        if (_isInShootingSequence)
        {
            _shootSequenceTime += dt;
            ProcessShootingSequence(dt);
        }

        // === ВРАЩЕНИЕ РОМБА ===
        if (_turret.CurrentState == ShootingTurret.TurretState.Reloading)
        {
            _targetDiamondRotation += dt * RotationSpeed;
            _frostLevel = Mathf.MoveToward(_frostLevel, FrostIntensity, dt * 2.0f);
        }
        else
        {
            float snapAngle = Mathf.Pi / 2f;
            float nearestSnap = Mathf.Round(_targetDiamondRotation / snapAngle) * snapAngle;
            _targetDiamondRotation = Mathf.Lerp(_targetDiamondRotation, nearestSnap, dt * 3f);
            _frostLevel = Mathf.MoveToward(_frostLevel, 0f, dt * 4.0f);
        }
        _diamondRotation = Mathf.Lerp(_diamondRotation, _targetDiamondRotation, dt * 8f);

        // === СХОЖДЕНИЕ ЛИНИЙ (плавное) ===
        _convergenceIntensity = Mathf.Lerp(_convergenceIntensity, _convergenceTarget, dt * ConvergenceSpeed);

        // === СЖАТИЕ И ОТДАЧА ===
        if (!_isInShootingSequence)
        {
            _squeezeOffset = Mathf.Lerp(_squeezeOffset, 0f, dt * ExpansionSpeed);
        }

        _recoilOffset = Mathf.Lerp(_recoilOffset, 0f, dt * RecoilDecay);

        // === ВСПЫШКА И КОЛЬЦО ===
        _shotFlash = Mathf.Lerp(_shotFlash, 0f, dt * FlashDecay);
        _impactRing = Mathf.Lerp(_impactRing, 0f, dt * 8f);

        // === ИТОГОВЫЙ SPREAD ===
        float targetTotal = _targetSpread + _squeezeOffset + _recoilOffset;
        float speed = targetTotal < _currentSpread ? SqueezeSpeed : ExpansionSpeed;
        _currentSpread = Mathf.Lerp(_currentSpread, targetTotal, dt * speed);

        // === ДАЛЬНОМЕР ===
        float realDist = GetTargetDistance();
        _targetDistanceDisplay = Mathf.Lerp(_targetDistanceDisplay, realDist, dt * 8f);

        UpdateShaderParams();
    }

    /// <summary>
    /// Обработка фаз выстрела.
    /// </summary>
    private void ProcessShootingSequence(float dt)
    {
        // Фаза 1: Схождение линий (0 - ConvergenceHoldTime)
        if (_shootSequenceTime < ConvergenceHoldTime)
        {
            // Линии плавно сходятся
            _convergenceTarget = 1.0f;

            // Небольшое предварительное сжатие
            float preSqueeze = Mathf.SmoothStep(0, 1, _shootSequenceTime / ConvergenceHoldTime);
            _squeezeOffset = Mathf.Lerp(0, SpreadShooting * 0.3f, preSqueeze);
        }
        // Фаза 2: Финальное сжатие (после ConvergenceHoldTime, до выстрела)
        else if (!_hasSqueezeTriggered)
        {
            float squeezeProgress = (_shootSequenceTime - ConvergenceHoldTime) / SqueezeDelay;
            squeezeProgress = Mathf.Clamp(squeezeProgress, 0, 1);

            // Ease-out сжатие (начинается медленно, ускоряется)
            float eased = 1f - Mathf.Pow(1f - squeezeProgress, 3f);
            _squeezeOffset = Mathf.Lerp(SpreadShooting * 0.3f, SpreadShooting, eased);

            // Схождение продолжается
            _convergenceTarget = Mathf.Lerp(1.0f, 0.3f, squeezeProgress);
        }
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

        // Эффекты выстрела
        _shaderMaterial.SetShaderParameter("convergence_intensity", _convergenceIntensity);
        _shaderMaterial.SetShaderParameter("shot_flash", _shotFlash);
        _shaderMaterial.SetShaderParameter("impact_ring", _impactRing);
        _shaderMaterial.SetShaderParameter("frost_level", _frostLevel);
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