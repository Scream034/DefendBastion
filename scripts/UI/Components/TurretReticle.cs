#nullable enable

using Godot;
using Game.Turrets;
using Game.UI.HUD;
using Game.Player;

namespace Game.UI.Components;

/// <summary>
/// Управляет процедурным (шейдерным) прицелом турели.
/// <para>
/// Отвечает за:
/// <list type="bullet">
/// <item>Визуализацию разброса (Spread) в зависимости от состояния турели.</item>
/// <item>Логику зума с изменением FOV камеры и эффектом пикселизации.</item>
/// <item>Анимацию выстрела (сжатие, вспышка, отдача).</item>
/// <item>Отображение дальномера и температуры (эффект инея).</item>
/// </list>
/// </para>
/// </summary>
public partial class TurretReticle : Control
{
    #region Configuration

    [ExportGroup("Visual Setup")]
    [Export] private ColorRect _reticleRect = null!;

    [ExportSubgroup("Layout Geometry")]
    /// <summary>Расстояние между элементами перекрестия.</summary>
    [Export] public float ReticleGap { get; set; } = 45f;
    /// <summary>Базовый размер центрального ромба.</summary>
    [Export] public float DiamondBaseSize { get; set; } = 12f;
    /// <summary>Отступ от краев экрана.</summary>
    [Export] public float EdgeMargin { get; set; } = 40f;
    /// <summary>Базовое количество пикселей на градус (для шкалы).</summary>
    [Export] public float BasePixelsPerDegree { get; set; } = 12f;

    [ExportGroup("Zoom System")]
    [Export] public float MinZoom { get; set; } = 1.0f;
    [Export] public float MaxZoom { get; set; } = 6.0f;
    [Export] public float ZoomStep { get; set; } = 0.5f;
    [Export] public float ZoomLerpSpeed { get; set; } = 6f;

    [ExportSubgroup("Camera Integration")]
    /// <summary>Поле зрения (FOV) при минимальном зуме (1x).</summary>
    [Export] public float BaseFov { get; set; } = 70f;
    /// <summary>Поле зрения (FOV) при максимальном зуме.</summary>
    [Export] public float MinFov { get; set; } = 12f;

    [ExportSubgroup("Visual Artifacts")]
    [Export] public float MaxPixelsPerDegree { get; set; } = 72f;
    /// <summary>Уровень зума, при котором начинается заметная пикселизация.</summary>
    [Export] public float ZoomPixelationStart { get; set; } = 2.0f;
    [Export] public float MaxPixelationIntensity { get; set; } = 0.7f;
    /// <summary>Интервал малых делений шкалы при максимальном зуме.</summary>
    [Export] public int MinorIntervalAtMaxZoom { get; set; } = 1;

    [ExportGroup("Reticle Dynamics")]
    [ExportSubgroup("Spread Values")]
    [Export] public float SpreadIdle { get; set; } = 0f;
    [Export] public float SpreadShooting { get; set; } = -1.0f; // Отрицательное значение может инвертировать логику в шейдере
    [Export] public float SpreadCooldown { get; set; } = 5f;
    [Export] public float SpreadReloading { get; set; } = 25f;
    [Export] public float SpreadNoAmmo { get; set; } = 15f;
    [Export] public float SpreadBroken { get; set; } = 40f;

    [ExportSubgroup("Movement Speed")]
    [Export] public float ExpansionSpeed { get; set; } = 10f;
    [Export] public float SqueezeSpeed { get; set; } = 40f;
    [Export] public float RotationSpeed { get; set; } = 4f;

    [ExportGroup("Feedback & FX")]
    [ExportSubgroup("Recoil")]
    [Export] public float RecoilImpulse { get; set; } = 40f;
    [Export] public float RecoilDecay { get; set; } = 5f;

    [ExportSubgroup("Shot Sequence")]
    [Export] public float ConvergenceHoldTime { get; set; } = 0.25f;
    [Export] public float ConvergenceSpeed { get; set; } = 3.0f;
    [Export] public float SqueezeDelay { get; set; } = 0.15f;
    [Export] public float FlashIntensity { get; set; } = 1.2f;
    [Export] public float FlashDecay { get; set; } = 15f;
    [Export] public float GlitchIntensity { get; set; } = 0.3f;

    [ExportSubgroup("Environment")]
    [Export] public float FrostIntensity { get; set; } = 0.8f;

    #endregion

    #region Internal State & Cache

    private PlayerControllableTurret? _turret;
    private ShaderMaterial? _shaderMaterial;

    // Animation Physics
    private float _currentSpread = 50f;
    private float _targetSpread = 0f;
    private float _recoilOffset = 0f;
    private float _squeezeOffset = 0f;
    private float _diamondRotation = 0f;
    private float _targetDiamondRotation = 0f;
    private float _stateTime = 0f;
    private int _currentShaderState = 0;

    // Recoil Timing (replacing Async/Timer)
    private float _recoilDelayTimer = 0f;
    private const float RecoilDelayDuration = 0.03f;

    // Zoom State
    private float _currentZoom = 1.0f;
    private float _targetZoom = 1.0f;
    private float _displayZoom = 1.0f;
    private int _baseMinorInterval = 5;

    // Shooting Sequence State
    private bool _isInShootingSequence = false;
    private float _shootSequenceTime = 0f;
    private bool _hasSqueezeTriggered = false;

    // Visual FX State
    private float _convergenceIntensity = 0f;
    private float _convergenceTarget = 0f;
    private float _shotFlash = 0f;
    private float _frostLevel = 0f;
    private float _impactRing = 0f;

    // Rangefinder
    private float _targetDistanceDisplay = 0f;

    #endregion

    #region Events & Properties

    /// <summary>
    /// Вызывается при изменении интенсивности прицеливания (разброса).
    /// </summary>
    public event System.Action<float>? OnAimingIntensityChanged;

    /// <summary>
    /// Вызывается при изменении зума.
    /// <param name="currentZoom">Текущий уровень зума.</param>
    /// <param name="pixelationIntensity">Расчитанная интенсивность пикселизации.</param>
    /// </summary>
    public event System.Action<float, float>? OnZoomChanged;

    public float CurrentZoom => _currentZoom;
    public float TargetZoom => _targetZoom;
    public float PixelationIntensity => CalculatePixelationIntensity(_currentZoom);

    #endregion

    #region Lifecycle

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        if (_reticleRect != null)
        {
            _shaderMaterial = _reticleRect.Material as ShaderMaterial;
            _reticleRect.SetAnchorsPreset(LayoutPreset.FullRect);
            _reticleRect.MouseFilter = MouseFilterEnum.Ignore;

            // Пытаемся сохранить дефолтный интервал из шейдера
            if (_shaderMaterial != null)
            {
                var interval = _shaderMaterial.GetShaderParameter(Constants.SP_TurretReticle_MinorInterval);
                if (interval.VariantType != Variant.Type.Nil)
                    _baseMinorInterval = (int)interval;
            }
        }

        // Гарантируем чистое состояние при старте
        Deinitialize();
    }

    public override void _ExitTree()
    {
        DisconnectSignals();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_turret == null || !Visible) return;
        float dt = (float)delta;

        _stateTime += dt;

        ProcessZoom(dt);
        ProcessShootingSequence(dt);
        ProcessReticleDynamics(dt);
        ProcessVisualEffects(dt);

        UpdateShaderParams();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_turret == null || !Visible) return;

        if (Input.IsActionJustPressed("zoom_in"))
        {
            ZoomIn();
            GetViewport().SetInputAsHandled();
        }
        else if (Input.IsActionJustPressed("zoom_out"))
        {
            ZoomOut();
            GetViewport().SetInputAsHandled();
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Инициализирует прицел для указанной турели и подписывается на события.
    /// </summary>
    public void Initialize(PlayerControllableTurret turret)
    {
        // Сначала очищаем старые подписки, если они были
        DisconnectSignals();

        _turret = turret;
        ResetInternalState();

        if (_turret != null)
        {
            _turret.OnStateChanged += OnTurretStateChanged;
            _turret.OnShot += OnTurretShot;
            UpdateTargetState();
        }

        UpdateShaderParams();
        UpdateCameraFov();
        _reticleRect.Visible = true;
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    /// <summary>
    /// Отключает прицел, отписывается от событий и скрывает UI.
    /// </summary>
    public void Deinitialize()
    {
        DisconnectSignals();
        _turret = null;
        _reticleRect.Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    /// <summary>
    /// Увеличивает зум на один шаг (ZoomStep).
    /// </summary>
    public void ZoomIn() => SetZoom(_targetZoom + ZoomStep);

    /// <summary>
    /// Уменьшает зум на один шаг (ZoomStep).
    /// </summary>
    public void ZoomOut() => SetZoom(_targetZoom - ZoomStep);

    /// <summary>
    /// Сбрасывает зум к минимальному значению.
    /// </summary>
    public void ResetZoom() => SetZoom(MinZoom);

    /// <summary>
    /// Устанавливает конкретное значение зума в пределах допустимого диапазона.
    /// </summary>
    public void SetZoom(float zoom)
    {
        _targetZoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        OnZoomChanged?.Invoke(_targetZoom, CalculatePixelationIntensity(_targetZoom));
    }

    /// <summary>
    /// Возвращает текущую отображаемую дистанцию дальномера.
    /// </summary>
    public float GetDisplayDistance() => _targetDistanceDisplay;

    /// <summary>
    /// Возвращает текущий сглаженный уровень зума (для UI).
    /// </summary>
    public float GetDisplayZoom() => _displayZoom;

    #endregion

    #region Logic & Processing

    private void DisconnectSignals()
    {
        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
        }
    }

    private void ResetInternalState()
    {
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
        _recoilDelayTimer = 0f;

        // Сброс зума
        _currentZoom = MinZoom;
        _targetZoom = MinZoom;
        _displayZoom = MinZoom;
    }

    private void ProcessZoom(float dt)
    {
        float prevZoom = _currentZoom;
        _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, dt * ZoomLerpSpeed);
        _displayZoom = Mathf.Lerp(_displayZoom, _targetZoom, dt * ZoomLerpSpeed * 1.5f);

        // Обновляем FOV камеры только если зум изменился существенно
        if (Mathf.Abs(_currentZoom - prevZoom) > 0.001f)
        {
            UpdateCameraFov();
        }
    }

    private void ProcessShootingSequence(float dt)
    {
        if (!_isInShootingSequence) return;

        _shootSequenceTime += dt;

        if (_shootSequenceTime < ConvergenceHoldTime)
        {
            // Фаза 1: Сведение перед выстрелом
            _convergenceTarget = 1.0f;
            float preSqueeze = Mathf.SmoothStep(0, 1, _shootSequenceTime / ConvergenceHoldTime);
            _squeezeOffset = Mathf.Lerp(0, SpreadShooting * 0.3f, preSqueeze);
        }
        else if (!_hasSqueezeTriggered)
        {
            // Фаза 2: Сжатие в момент готовности
            float squeezeProgress = (_shootSequenceTime - ConvergenceHoldTime) / SqueezeDelay;
            squeezeProgress = Mathf.Clamp(squeezeProgress, 0, 1);
            float eased = 1f - Mathf.Pow(1f - squeezeProgress, 3f);
            _squeezeOffset = Mathf.Lerp(SpreadShooting * 0.3f, SpreadShooting, eased);
            _convergenceTarget = Mathf.Lerp(1.0f, 0.3f, squeezeProgress);
        }
    }

    private void ProcessReticleDynamics(float dt)
    {
        if (_turret == null) return;

        // 1. Вращение ромба (при перезарядке)
        if (_turret.CurrentState == ShootingTurret.TurretState.Reloading)
        {
            _targetDiamondRotation += dt * RotationSpeed;
            _frostLevel = Mathf.MoveToward(_frostLevel, FrostIntensity, dt * 2.0f);
        }
        else
        {
            // Возврат к ближайшему углу 90 градусов
            float snapAngle = Mathf.Pi / 2f;
            float nearestSnap = Mathf.Round(_targetDiamondRotation / snapAngle) * snapAngle;
            _targetDiamondRotation = Mathf.Lerp(_targetDiamondRotation, nearestSnap, dt * 3f);
            _frostLevel = Mathf.MoveToward(_frostLevel, 0f, dt * 4.0f);
        }
        _diamondRotation = Mathf.Lerp(_diamondRotation, _targetDiamondRotation, dt * 8f);

        // 2. Схождение линий (Convergence)
        _convergenceIntensity = Mathf.Lerp(_convergenceIntensity, _convergenceTarget, dt * ConvergenceSpeed);

        // 3. Отдача (с задержкой)
        if (_recoilDelayTimer > 0)
        {
            _recoilDelayTimer -= dt;
            if (_recoilDelayTimer <= 0)
            {
                _recoilOffset = RecoilImpulse;
            }
        }

        // 4. Затухание смещений
        if (!_isInShootingSequence)
        {
            _squeezeOffset = Mathf.Lerp(_squeezeOffset, 0f, dt * ExpansionSpeed);
        }
        _recoilOffset = Mathf.Lerp(_recoilOffset, 0f, dt * RecoilDecay);

        // 5. Итоговый Spread
        float targetTotal = _targetSpread + _squeezeOffset + _recoilOffset;
        float speed = targetTotal < _currentSpread ? SqueezeSpeed : ExpansionSpeed;
        _currentSpread = Mathf.Lerp(_currentSpread, targetTotal, dt * speed);

        // 6. Дальномер
        _targetDistanceDisplay = Mathf.Lerp(_targetDistanceDisplay, LocalPlayer.Instance.Head.GetScannerDistance(), dt * 8f);
    }

    private void ProcessVisualEffects(float dt)
    {
        _shotFlash = Mathf.Lerp(_shotFlash, 0f, dt * FlashDecay);
        _impactRing = Mathf.Lerp(_impactRing, 0f, dt * 8f);
    }

    private void UpdateShaderParams()
    {
        if (_shaderMaterial == null || _turret == null) return;

        // Базовая геометрия
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_ReticleGap, ReticleGap);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_DiamondSize, DiamondBaseSize);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_EdgeMargin, EdgeMargin);

        // Вращение турели
        float currentYawDeg = Mathf.RadToDeg(_turret.TurretYaw.Rotation.Y);
        float currentPitchDeg = Mathf.RadToDeg(_turret.TurretPitch.Rotation.X);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_Yaw, currentYawDeg);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_Pitch, currentPitchDeg);

        // Динамика
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_Spread, _currentSpread);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_DiamondRot, _diamondRotation);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_TurretState, _currentShaderState);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_StateTime, _stateTime);

        // Зум-зависимые параметры
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_PPD, CalculatePixelsPerDegree(_currentZoom));
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_MinorInterval, CalculateMinorInterval(_currentZoom));
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_ZoomLevel, _currentZoom);

        // Эффекты
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_Convergence, _convergenceIntensity);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_ShotFlash, _shotFlash);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_ImpactRing, _impactRing);
        _shaderMaterial.SetShaderParameter(Constants.SP_TurretReticle_Frost, _frostLevel);
    }

    #endregion

    #region Event Handlers

    private void OnTurretStateChanged(ShootingTurret.TurretState newState)
    {
        _stateTime = 0f;

        if (newState == ShootingTurret.TurretState.Shooting)
        {
            StartShootingSequence();
        }
        else
        {
            _isInShootingSequence = false;
        }

        UpdateTargetState();
    }

    private void OnTurretShot()
    {
        _squeezeOffset = SpreadShooting * 1.5f;
        _hasSqueezeTriggered = true;
        _shotFlash = FlashIntensity;
        _impactRing = 1.0f;

        _convergenceIntensity = 0f;
        _convergenceTarget = 0f;

        // Запускаем таймер задержки визуальной отдачи
        _recoilDelayTimer = RecoilDelayDuration;

        // Глитч сильнее при большом зуме
        float glitchMult = 1f + (_currentZoom - MinZoom) / (MaxZoom - MinZoom) * 0.5f;
        SharedHUD.TriggerColoredGlitch(
            new Color(0.3f, 0.9f, 0.85f, 1f),
            GlitchIntensity * glitchMult,
            0.12f
        );
    }

    #endregion

    #region Helpers

    private void StartShootingSequence()
    {
        _isInShootingSequence = true;
        _shootSequenceTime = 0f;
        _hasSqueezeTriggered = false;
        _convergenceTarget = 1.0f;
        _convergenceIntensity = 0f;
    }

    private void UpdateTargetState()
    {
        if (_turret == null) return;

        // Определение состояния для шейдера и целевого разброса
        // 0=Idle, 1=Cooldown, 2=Reload, 3=NoAmmo, 4=Broken, 5=Shooting
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

        // Рассчитываем и сообщаем внешним системам об интенсивности прицеливания (0..1)
        float maxSpread = Mathf.Max(SpreadBroken, SpreadReloading);
        float aimingIntensity = 1.0f - Mathf.Clamp(_targetSpread / maxSpread, 0f, 1f);
        OnAimingIntensityChanged?.Invoke(aimingIntensity);
    }

    private void UpdateCameraFov()
    {
        var cam = _turret!.CameraController?.GetCamera();
        if (cam == null) return;

        float t = (_currentZoom - MinZoom) / (MaxZoom - MinZoom);
        float targetFov = Mathf.Lerp(BaseFov, MinFov, t);
        cam.Fov = targetFov;
    }

    private float CalculatePixelationIntensity(float zoom)
    {
        if (zoom <= ZoomPixelationStart)
            return 0f;

        float t = (zoom - ZoomPixelationStart) / (MaxZoom - ZoomPixelationStart);
        // Квадратичное сглаживание (ease-in)
        t = t * t;
        return Mathf.Clamp(t * MaxPixelationIntensity, 0f, MaxPixelationIntensity);
    }

    private float CalculatePixelsPerDegree(float zoom)
    {
        float t = (zoom - MinZoom) / (MaxZoom - MinZoom);
        // Ease-out для быстрого отклика в начале зума
        t = 1f - Mathf.Pow(1f - t, 2f);
        return Mathf.Lerp(BasePixelsPerDegree, MaxPixelsPerDegree, t);
    }

    private int CalculateMinorInterval(float zoom)
    {
        float t = (zoom - MinZoom) / (MaxZoom - MinZoom);
        return Mathf.RoundToInt(Mathf.Lerp(_baseMinorInterval, MinorIntervalAtMaxZoom, t));
    }

    #endregion
}