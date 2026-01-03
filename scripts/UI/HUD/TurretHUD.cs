#nullable enable

using Godot;
using Game.Turrets;
using Game.UI.Components;
using Game.Player;

namespace Game.UI.HUD;

/// <summary>
/// Управляет проекционным дисплеем (HUD) турели.
/// Отвечает за визуализацию состояния, прицела, сенсоров и эффектов перегрева.
/// </summary>
public partial class TurretHUD : Control
{
    #region Dependencies

    [ExportGroup("Components")]
    [Export] private AnimationPlayer _animPlayer = null!;
    [Export] private TurretReticle _reticle = null!;
    [Export] private ColorRect _gridOverlay = null!;
    [Export] private ZoomPixelationOverlay _pixelationOverlay = null!;

    [ExportGroup("Sensor Panel")]
    [Export] private SensorDataPanel _sensorPanel = null!;
    [Export] private Label _statusLabel = null!;

    [ExportGroup("Temperature Sensor")]
    [Export] private TemperatureSensorEmitter _tempSensor = null!;

    #endregion

    #region Visual Settings

    [ExportGroup("Grid Settings")]
    [Export] private float _gridFadeSpeed = 5f;
    [Export] private float _gridMinIntensity = 1.0f;
    [Export] private float _gridMaxIntensity = 3.0f;
    [Export] private Color _gridFlashColorNormal = new(1f, 0.95f, 0.8f, 0.4f);
    [Export] private Color _gridFlashColorCooling = new(0.4f, 0.7f, 1f, 0.3f);
    [Export] private Color _gridFlashColorCritical = new(1f, 0.2f, 0.1f, 0.5f);

    [ExportGroup("Status Animation")]
    [Export] private float _statusFadeDuration = 0.15f;
    [Export] private float _statusTypeSpeed = 40f;

    [ExportGroup("Color Palette")]
    [Export] private Color _colorNormal = new(0.2f, 0.9f, 0.8f);     // Cyan
    [Export] private Color _colorWarning = new(1f, 0.7f, 0.2f);      // Orange
    [Export] private Color _colorCritical = new(1f, 0.3f, 0.2f);     // Red
    [Export] private Color _colorAction = new(1f, 0.95f, 0.8f);      // Off-White/Yellowish
    [Export] private Color _colorCooling = new(0.5f, 0.8f, 1f);      // Light Blue

    [ExportGroup("Status Texts")]
    [Export] private string _txtOnline = "SYSTEM ONLINE";
    [Export] private string _txtOverheat = "⚠ OVERHEAT";
    [Export] private string _txtCriticalHeat = "⚠ CRITICAL HEAT";
    [Export] private string _txtFiring = "● FIRING";
    [Export] private string _txtCooling = "❄ COOLING";
    [Export] private string _txtCriticalErr = "✕ CRITICAL";
    [Export] private string _txtCycling = "○ CYCLING";
    [Export] private string _txtReady = "● READY";
    [Export] private string _txtNoAmmo = "⚠ NO AMMO";
    [Export] private string _txtLowAmmo = "⚠ LOW AMMO";

    #endregion

    #region Internal State

    // Ключи данных панели
    private const string KeyDist = "dist";
    private const string KeyAmmo = "ammo";
    private const string KeyTemp = "temp";
    private const string KeyHull = "hull";

    private ShaderMaterial? _gridMaterial;
    private float _currentGridIntensity = 0f;
    private float _targetGridIntensity = 0f;

    private PlayerControllableTurret? _turret;

    private Tween? _statusTween;
    private Tween? _gridFlashTween;

    private string _targetStatusText = "";
    private ShootingTurret.TurretState _lastState;

    private bool _wasOverheatWarning;
    private bool _wasCriticalOverheat;

    #endregion

    #region Lifecycle

    public override void _Ready()
    {
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);

        if (_gridOverlay != null)
            _gridMaterial = _gridOverlay.Material as ShaderMaterial;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_turret == null) return;
        float dt = (float)delta;

        // Плавное изменение интенсивности сетки
        _currentGridIntensity = Mathf.Lerp(_currentGridIntensity, _targetGridIntensity, dt * _gridFadeSpeed);
        UpdateGridIntensity(_currentGridIntensity);

        UpdateSensorData();

        // Обновление пикселизации на основе зума
        if (_reticle != null)
        {
            _pixelationOverlay?.SetIntensity(_reticle.PixelationIntensity, _reticle.CurrentZoom);
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Инициализирует и отображает HUD для указанной турели.
    /// </summary>
    public void ShowHUD(PlayerControllableTurret turret)
    {
        _turret = turret;
        Visible = true;

        ConfigureEnvironment();
        ConnectSignals();
        ResetState();
        InitializeVisuals();

        AnimateStatus(_txtOnline, _colorNormal);
        _animPlayer?.Play(Constants.AnimPlayer_HUD_Boot);

        SetProcess(true);
        SetPhysicsProcess(true);
    }

    /// <summary>
    /// Скрывает HUD и отключает обработку событий.
    /// </summary>
    public void HideHUD()
    {
        KillTweens();
        DisconnectSignals();

        _reticle?.Deinitialize();
        _tempSensor?.Deinitialize();
        _pixelationOverlay?.Reset();
        _sensorPanel?.Clear();

        _turret = null;
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    #endregion

    #region Initialization & Cleanup

    private void ConfigureEnvironment()
    {
        SharedHUD.SetLoggerPreset(LoggerPreset.FullLessLines);
        SharedHUD.SetLoggerVisible(true);
        RobotBus.Net("INIT: NEURAL_LINK_ESTABLISHED");
    }

    private void ConnectSignals()
    {
        if (_reticle != null && _turret != null)
        {
            _reticle.Initialize(_turret);
            _reticle.OnAimingIntensityChanged += OnAimingIntensityChanged;
            _reticle.OnZoomChanged += OnZoomChanged;
        }

        if (_tempSensor != null && _turret != null)
        {
            _tempSensor.Initialize(_turret);
            _tempSensor.OnOverheatWarning += OnOverheatWarning;
            _tempSensor.OnCriticalOverheat += OnCriticalOverheat;
            _tempSensor.OnOverheatDamage += OnOverheatDamage;
        }

        if (_turret != null)
        {
            _turret.OnStateChanged += OnTurretStateChanged;
            _turret.OnShot += OnTurretShot;
            _turret.OnAmmoChanged += OnAmmoChanged;
            _lastState = _turret.CurrentState;
        }
    }

    private void DisconnectSignals()
    {
        if (_reticle != null)
        {
            _reticle.OnAimingIntensityChanged -= OnAimingIntensityChanged;
            _reticle.OnZoomChanged -= OnZoomChanged;
        }

        if (_tempSensor != null)
        {
            _tempSensor.OnOverheatWarning -= OnOverheatWarning;
            _tempSensor.OnCriticalOverheat -= OnCriticalOverheat;
            _tempSensor.OnOverheatDamage -= OnOverheatDamage;
        }

        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
            _turret.OnAmmoChanged -= OnAmmoChanged;
        }
    }

    private void ResetState()
    {
        _targetGridIntensity = _gridMinIntensity;
        UpdateGridIntensity(0f);
        _wasOverheatWarning = false;
        _wasCriticalOverheat = false;
        _pixelationOverlay?.Reset();
    }

    private void InitializeVisuals()
    {
        if (_sensorPanel == null || _turret == null) return;
        _sensorPanel.SetLine(KeyDist, "DIST", "----m");
        UpdateAmmoDisplay();
        UpdateTemperatureDisplay();
        UpdateIntegrityDisplay();
    }

    private void KillTweens()
    {
        _statusTween?.Kill();
        _gridFlashTween?.Kill();
    }

    #endregion

    #region Event Handlers

    private void OnAimingIntensityChanged(float intensity)
    {
        _targetGridIntensity = Mathf.Lerp(_gridMinIntensity, _gridMaxIntensity, intensity);
    }

    private void OnZoomChanged(float zoom, float pixelationIntensity)
    {
        _pixelationOverlay?.SetIntensity(pixelationIntensity, zoom);
        _turret!.CameraController.AdjustSensitivityByZoomLevel(zoom);

        if (zoom > 1.5f)
        {
            RobotBus.Sys($"ZOOM: {zoom:F1}x");
        }
    }

    private void OnOverheatWarning()
    {
        if (_wasOverheatWarning) return;
        _wasOverheatWarning = true;

        AnimateStatus(_txtOverheat, _colorWarning);
        _sensorPanel?.Flash(_colorWarning);
        _sensorPanel?.SetAlertLevel(0.5f);

        RobotBus.Warn("THERMAL_WARNING: CORE_TEMP_ELEVATED");
    }

    private void OnCriticalOverheat()
    {
        _wasCriticalOverheat = true;

        AnimateStatus(_txtCriticalHeat, _colorCritical);
        _sensorPanel?.Flash(_colorCritical);
        _sensorPanel?.SetAlertLevel(1f);
        SharedHUD.TriggerColoredGlitch(_colorWarning, 0.5f, 0.2f);

        RobotBus.Warn("THERMAL_CRITICAL: SYSTEM_DAMAGE_IMMINENT");
    }

    private void OnOverheatDamage(float damage)
    {
        // Периодический глитч при получении урона от перегрева
        if (Time.GetTicksMsec() % 500 < 50)
        {
            SharedHUD.TriggerColoredGlitch(new Color(1f, 0.4f, 0.1f), 0.3f, 0.1f);
        }
    }

    private void OnTurretStateChanged(ShootingTurret.TurretState state)
    {
        if (state == _lastState) return;
        _lastState = state;

        // Сброс флагов при перезарядке/охлаждении
        if (state == ShootingTurret.TurretState.Reloading)
        {
            _wasOverheatWarning = false;
            _wasCriticalOverheat = false;
        }

        switch (state)
        {
            case ShootingTurret.TurretState.Shooting:
                AnimateStatus(_txtFiring, _colorAction);
                FlashGrid(_gridFlashColorNormal, 0.1f);
                _sensorPanel?.Flash(_colorAction);
                break;

            case ShootingTurret.TurretState.Reloading:
                AnimateStatus(_txtCooling, _colorCooling);
                FlashGrid(_gridFlashColorCooling, 0.15f);
                _sensorPanel?.SetAlertLevel(0f);
                break;

            case ShootingTurret.TurretState.Broken:
                AnimateStatus(_txtCriticalErr, _colorCritical);
                FlashGrid(_gridFlashColorCritical, 0.5f);
                SharedHUD.TriggerGlitch(0.8f, 0.3f);
                _sensorPanel?.SetAlertLevel(1f);
                break;

            case ShootingTurret.TurretState.FiringCooldown:
                AnimateStatus(_txtCycling, _colorNormal with { A = 0.7f });
                break;

            case ShootingTurret.TurretState.Idle:
                _sensorPanel?.SetAlertLevel(0f);
                if (_turret != null && _turret.CanShoot)
                    AnimateStatus(_txtReady, _colorNormal);
                else if (_turret != null && !_turret.HasAmmoInMag)
                    AnimateStatus(_txtNoAmmo, _colorCritical);
                break;
        }
    }

    private void OnTurretShot()
    {
        RobotBus.Combat("SHOT_FIRED");
        FlashGrid(new Color(1f, 0.95f, 0.8f, 0.3f), 0.05f);
    }

    private void OnAmmoChanged()
    {
        UpdateAmmoDisplay();

        if (_turret != null && !_turret.HasInfiniteAmmo)
        {
            float ratio = (float)_turret.CurrentAmmo / _turret.MagazineSize;
            if (ratio <= 0.1f && _turret.CurrentAmmo > 0)
            {
                AnimateStatus(_txtLowAmmo, _colorWarning);
            }
        }
    }

    #endregion

    #region UI Updates & Animation

    private void UpdateSensorData()
    {
        if (_sensorPanel == null || _turret == null) return;

        // Дистанция
        if (_reticle != null)
        {
            float dist = _reticle.GetDisplayDistance();
            string distStr = dist > LocalPlayer.Instance.Head.GetScannerMaxDistance() ? "----m" : $"{dist:0000}m";
            _sensorPanel.SetLine(KeyDist, "DIST", distStr);
        }

        UpdateTemperatureDisplay();
        UpdateIntegrityDisplay();
    }

    private void UpdateTemperatureDisplay()
    {
        if (_sensorPanel == null || _tempSensor == null) return;

        float tempCelsius = _tempSensor.GetReadingCelsius();
        Color tempColor;

        if (_tempSensor.IsOverheating)
        {
            float blink = (Mathf.Sin(Time.GetTicksMsec() / 80f) + 1f) * 0.5f;
            tempColor = _colorCritical.Lerp(new Color(_colorCritical, 0.1f), blink);
        }
        else if (_tempSensor.IsWarning)
        {
            tempColor = _colorWarning;
        }
        else
        {
            // Градиент от Cyan к Yellow
            float t = _tempSensor.NormalizedTemperature * 2f;
            tempColor = _colorNormal.Lerp(_colorWarning, t);
        }

        _sensorPanel.SetLine(KeyTemp, "TEMP", $"{tempCelsius:F0}°C", tempColor);
    }

    private void UpdateAmmoDisplay()
    {
        if (_sensorPanel == null || _turret == null) return;

        if (_turret.HasInfiniteAmmo)
        {
            _sensorPanel.SetLine(KeyAmmo, "AMMO", "∞", _colorNormal);
        }
        else
        {
            string ammoStr = $"{_turret.CurrentAmmo:D2}/{_turret.MagazineSize:D2}";
            float ratio = (float)_turret.CurrentAmmo / _turret.MagazineSize;

            Color color = ratio > 0.3f ? _colorNormal : (ratio > 0.1f ? _colorWarning : _colorCritical);
            _sensorPanel.SetLine(KeyAmmo, "AMMO", ammoStr, color);
        }
    }

    private void UpdateIntegrityDisplay()
    {
        if (_sensorPanel == null || _turret == null) return;

        float integrity = _turret.Health / _turret.MaxHealth * 100f;

        Color color;
        if (integrity <= 10f)
        {
            float blink = (Mathf.Sin(Time.GetTicksMsec() / 100f) + 1f) * 0.5f;
            color = _colorCritical.Lerp(new Color(_colorCritical, 0.1f), blink);
        }
        else if (integrity <= 30f)
        {
            color = _colorWarning;
        }
        else
        {
            color = _colorNormal;
        }

        _sensorPanel.SetLine(KeyHull, "HULL", $"{integrity:F0}%", color);
    }

    private void AnimateStatus(string text, Color color)
    {
        if (_statusLabel == null) return;

        _statusTween?.Kill();
        _statusTween = CreateTween();
        _statusTween.SetParallel(true);

        _statusTween.TweenProperty(_statusLabel, "modulate", color, _statusFadeDuration);

        _targetStatusText = text;

        // Расчет времени печати текста
        float typeDuration = text.Length / _statusTypeSpeed;

        // Используем метод для эффекта печатной машинки
        _statusTween.TweenMethod(
            Callable.From<int>(UpdateStatusText),
            0, text.Length, typeDuration
        );
    }

    private void UpdateStatusText(int charCount)
    {
        if (_statusLabel == null || string.IsNullOrEmpty(_targetStatusText)) return;

        string displayed = _targetStatusText[..Mathf.Min(charCount, _targetStatusText.Length)];

        // Добавляем мигающий курсор, если текст еще печатается
        if (charCount < _targetStatusText.Length)
        {
            displayed += (Time.GetTicksMsec() / 100 % 2 == 0) ? "_" : " ";
        }

        _statusLabel.Text = displayed;
    }

    private void UpdateGridIntensity(float intensity)
    {
        _gridMaterial?.SetShaderParameter(Constants.SP_GridOverlay_Intensity, intensity);
    }

    /// <summary>
    /// Создает эффект вспышки сетки заданного цвета.
    /// Использует Tween вместо async/timer для безопасности при уничтожении узла.
    /// </summary>
    private void FlashGrid(Color flashColor, float duration)
    {
        if (_gridMaterial == null) return;

        // Сохраняем исходные значения, если нужно, или просто перебиваем текущие
        var targetLarge = flashColor;
        var targetSmall = flashColor * 0.7f;

        // Получаем текущие цвета из материала для плавного перехода, или сбрасываем в дефолт
        // В данном случае мы просто устанавливаем вспышку и возвращаем обратно.

        // Важно: Мы предполагаем, что "нормальный" цвет сетки определен в шейдере или в настройках по умолчанию.
        // Для простоты реализации вспышки, мы мгновенно ставим цвет, и твиним его прозрачность или возвращаем исходный.

        // Более безопасный подход: просто меняем цвет, ждем, возвращаем обратно.
        // Так как originalLarge может меняться динамически в шейдере, лучше просто делать "наложение"
        // Но шейдеры обычно стейтлесс. Возьмем текущие параметры.

        var originalLarge = (Color)_gridMaterial.GetShaderParameter(Constants.SP_GridOverlay_GridColorLarge);
        var originalSmall = (Color)_gridMaterial.GetShaderParameter(Constants.SP_GridOverlay_GridColorSmall);

        _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorLarge, targetLarge);
        _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorSmall, targetSmall);

        _gridFlashTween?.Kill();
        _gridFlashTween = CreateTween();

        _gridFlashTween.TweenInterval(duration);
        _gridFlashTween.TweenCallback(Callable.From(() =>
        {
            if (_gridMaterial != null) // Проверка на случай удаления материала
            {
                _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorLarge, originalLarge);
                _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorSmall, originalSmall);
            }
        }));
    }

    #endregion
}