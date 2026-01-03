#nullable enable

using Godot;
using Game.Turrets;
using Game.UI.Components;

namespace Game.UI.HUD;

public partial class TurretHUD : Control
{
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

    [ExportGroup("Grid Settings")]
    [Export] private float _gridFadeSpeed = 5f;
    [Export] private float _gridMinIntensity = 1.0f;
    [Export] private float _gridMaxIntensity = 3.0f;

    [ExportGroup("Status Animation")]
    [Export] private float _statusFadeDuration = 0.15f;
    [Export] private float _statusTypeSpeed = 40f;

    private ShaderMaterial? _gridMaterial;
    private float _currentGridIntensity = 0f;
    private float _targetGridIntensity = 0f;

    private PlayerControllableTurret? _turret;

    private Tween? _statusTween;
    private string _targetStatusText = "";
    private int _displayedCharCount = 0;
    private ShootingTurret.TurretState _lastState;
    
    // Отслеживание предупреждений температуры
    private bool _wasOverheatWarning = false;
    private bool _wasCriticalOverheat = false;

    // Ключи сенсорных данных
    private const string KEY_DIST = "dist";
    private const string KEY_AMMO = "ammo";
    private const string KEY_TEMP = "temp";
    private const string KEY_HULL = "hull";

    public override void _Ready()
    {
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);

        if (_gridOverlay != null)
            _gridMaterial = _gridOverlay.Material as ShaderMaterial;
    }

    public void ShowHUD(PlayerControllableTurret turret, TurretCameraController camera)
    {
        _turret = turret;
        Visible = true;

        SharedHUD.SetLoggerPreset(LoggerPreset.FullLessLines);
        SharedHUD.SetLoggerVisible(true);

        RobotBus.Net("INIT: NEURAL_LINK_ESTABLISHED");

        // Инициализация прицела
        if (_reticle != null)
        {
            _reticle.Initialize(turret, camera);
            _reticle.OnAimingIntensityChanged += OnAimingIntensityChanged;
            _reticle.OnZoomChanged += OnZoomChanged;
        }

        // Инициализация температурного датчика
        if (_tempSensor != null)
        {
            _tempSensor.Initialize(turret);
            _tempSensor.OnOverheatWarning += OnOverheatWarning;
            _tempSensor.OnCriticalOverheat += OnCriticalOverheat;
            _tempSensor.OnOverheatDamage += OnOverheatDamage;
        }

        _pixelationOverlay?.Reset();

        if (_turret != null)
        {
            _turret.OnStateChanged += OnTurretStateChanged;
            _turret.OnShot += OnTurretShot;
            _turret.OnAmmoChanged += OnAmmoChanged;

            _lastState = _turret.CurrentState;
        }

        _targetGridIntensity = _gridMinIntensity;
        UpdateGridIntensity(0f);
        
        _wasOverheatWarning = false;
        _wasCriticalOverheat = false;
        
        InitializeSensorPanel();

        AnimateStatus("SYSTEM ONLINE", new Color(0.2f, 0.9f, 0.8f, 0.9f));

        _animPlayer?.Play("Boot");
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    public void HideHUD()
    {
        _statusTween?.Kill();

        if (_reticle != null)
        {
            _reticle.OnAimingIntensityChanged -= OnAimingIntensityChanged;
            _reticle.OnZoomChanged -= OnZoomChanged;
            _reticle.Deinitialize();
        }

        if (_tempSensor != null)
        {
            _tempSensor.OnOverheatWarning -= OnOverheatWarning;
            _tempSensor.OnCriticalOverheat -= OnCriticalOverheat;
            _tempSensor.OnOverheatDamage -= OnOverheatDamage;
            _tempSensor.Deinitialize();
        }

        _pixelationOverlay?.Reset();
        _sensorPanel?.Clear();

        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
            _turret.OnAmmoChanged -= OnAmmoChanged;
            _turret = null;
        }

        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    private void InitializeSensorPanel()
    {
        if (_sensorPanel == null || _turret == null) return;
        
        _sensorPanel.SetLine(KEY_DIST, "DIST", "----m");
        UpdateAmmoDisplay();
        UpdateTemperatureDisplay();
        UpdateIntegrityDisplay();
    }

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
        
        AnimateStatus("⚠ OVERHEAT", new Color(1f, 0.7f, 0.2f, 1f));
        _sensorPanel?.Flash(new Color(1f, 0.7f, 0.2f));
        _sensorPanel?.SetAlertLevel(0.5f);
        
        RobotBus.Warn("THERMAL_WARNING: CORE_TEMP_ELEVATED");
    }

    private void OnCriticalOverheat()
    {
        _wasCriticalOverheat = true;
        
        AnimateStatus("⚠ CRITICAL HEAT", new Color(1f, 0.3f, 0.2f, 1f));
        _sensorPanel?.Flash(new Color(1f, 0.3f, 0.2f));
        _sensorPanel?.SetAlertLevel(1f);
        SharedHUD.TriggerColoredGlitch(new Color(1f, 0.5f, 0.2f), 0.5f, 0.2f);
        
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

        // Сброс флагов предупреждения при охлаждении
        if (state == ShootingTurret.TurretState.Reloading)
        {
            _wasOverheatWarning = false;
            _wasCriticalOverheat = false;
        }

        switch (state)
        {
            case ShootingTurret.TurretState.Shooting:
                AnimateStatus("● FIRING", new Color(1f, 0.95f, 0.8f, 1f));
                FlashGrid(new Color(1f, 0.95f, 0.8f, 0.4f), 0.1f);
                _sensorPanel?.Flash(new Color(1f, 0.95f, 0.8f));
                break;

            case ShootingTurret.TurretState.Reloading:
                AnimateStatus("❄ COOLING", new Color(0.5f, 0.8f, 1f, 0.9f));
                FlashGrid(new Color(0.4f, 0.7f, 1f, 0.3f), 0.15f);
                _sensorPanel?.SetAlertLevel(0f);
                break;

            case ShootingTurret.TurretState.Broken:
                AnimateStatus("✕ CRITICAL", new Color(1f, 0.2f, 0.1f, 0.9f));
                FlashGrid(new Color(1f, 0.2f, 0.1f, 0.5f), 0.5f);
                SharedHUD.TriggerGlitch(0.8f, 0.3f);
                _sensorPanel?.SetAlertLevel(1f);
                break;

            case ShootingTurret.TurretState.FiringCooldown:
                AnimateStatus("○ CYCLING", new Color(0.2f, 0.9f, 0.8f, 0.7f));
                break;

            case ShootingTurret.TurretState.Idle:
                _sensorPanel?.SetAlertLevel(0f);
                if (_turret != null && _turret.CanShoot)
                    AnimateStatus("● READY", new Color(0.2f, 0.9f, 0.8f, 0.9f));
                else if (_turret != null && !_turret.HasAmmoInMag)
                    AnimateStatus("⚠ NO AMMO", new Color(1f, 0.3f, 0.2f, 0.9f));
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
                AnimateStatus("⚠ LOW AMMO", new Color(1f, 0.5f, 0.2f, 0.9f));
            }
        }
    }

    #endregion

    public override void _PhysicsProcess(double delta)
    {
        if (_turret == null) return;
        float dt = (float)delta;

        _currentGridIntensity = Mathf.Lerp(_currentGridIntensity, _targetGridIntensity, dt * _gridFadeSpeed);
        UpdateGridIntensity(_currentGridIntensity);

        UpdateSensorData();

        if (_reticle != null)
        {
            _pixelationOverlay?.SetIntensity(_reticle.PixelationIntensity, _reticle.CurrentZoom);
        }
    }

    private void UpdateSensorData()
    {
        if (_sensorPanel == null || _turret == null) return;

        // Дистанция
        if (_reticle != null)
        {
            float dist = _reticle.GetDisplayDistance();
            string distStr = dist > 2000 ? "----m" : $"{dist:0000}m";
            _sensorPanel.SetLine(KEY_DIST, "DIST", distStr);
        }

        // Температура
        UpdateTemperatureDisplay();
        
        // Целостность
        UpdateIntegrityDisplay();
    }

    private void UpdateTemperatureDisplay()
    {
        if (_sensorPanel == null || _tempSensor == null) return;

        float tempCelsius = _tempSensor.GetReadingCelsius();
        
        // Определяем цвет на основе порогов
        Color tempColor;
        if (_tempSensor.IsOverheating)
        {
            // Мигание при критическом перегреве
            float blink = (Mathf.Sin(Time.GetTicksMsec() / 80f) + 1f) * 0.5f;
            tempColor = new Color(1f, Mathf.Lerp(0.1f, 0.3f, blink), 0.1f);
        }
        else if (_tempSensor.IsWarning)
        {
            tempColor = new Color(1f, 0.7f, 0.2f);
        }
        else
        {
            // Градиент от циана к жёлтому
            float t = _tempSensor.NormalizedTemperature * 2f; // 0-0.5 это нормальный диапазон
            tempColor = new Color(
                Mathf.Lerp(0.2f, 1f, t),
                Mathf.Lerp(0.9f, 0.8f, t),
                Mathf.Lerp(0.8f, 0.2f, t)
            );
        }

        _sensorPanel.SetLine(KEY_TEMP, "TEMP", $"{tempCelsius:F0}°C", tempColor);
    }

    private void UpdateAmmoDisplay()
    {
        if (_sensorPanel == null || _turret == null) return;

        if (_turret.HasInfiniteAmmo)
        {
            _sensorPanel.SetLine(KEY_AMMO, "AMMO", "∞", new Color(0.2f, 0.9f, 0.8f));
        }
        else
        {
            string ammoStr = $"{_turret.CurrentAmmo:D2}/{_turret.MagazineSize:D2}";
            float ratio = (float)_turret.CurrentAmmo / _turret.MagazineSize;
            
            Color color = ratio > 0.3f
                ? new Color(0.2f, 0.9f, 0.8f)
                : ratio > 0.1f
                    ? new Color(1f, 0.7f, 0.2f)
                    : new Color(1f, 0.3f, 0.2f);
            
            _sensorPanel.SetLine(KEY_AMMO, "AMMO", ammoStr, color);
        }
    }

    private void UpdateIntegrityDisplay()
    {
        if (_sensorPanel == null || _turret == null) return;

        float integrity = (_turret.Health / _turret.MaxHealth) * 100f;
        
        Color color;
        if (integrity <= 10f)
        {
            // Мигание при критической целостности
            float blink = (Mathf.Sin(Time.GetTicksMsec() / 100f) + 1f) * 0.5f;
            color = new Color(1f, Mathf.Lerp(0.1f, 0.3f, blink), 0.2f);
        }
        else if (integrity <= 30f)
        {
            color = new Color(1f, 0.7f, 0.2f);
        }
        else
        {
            color = new Color(0.2f, 0.9f, 0.8f);
        }

        _sensorPanel.SetLine(KEY_HULL, "HULL", $"{integrity:F0}%", color);
    }

    #region Status Animation

    private void AnimateStatus(string text, Color color)
    {
        if (_statusLabel == null) return;

        _statusTween?.Kill();
        _statusTween = CreateTween();
        _statusTween.SetParallel(true);

        _statusTween.TweenProperty(_statusLabel, "modulate", color, _statusFadeDuration);

        _targetStatusText = text;
        _displayedCharCount = 0;

        float typeDuration = text.Length / _statusTypeSpeed;

        _statusTween.TweenMethod(
            Callable.From<int>(UpdateStatusText),
            0, text.Length, typeDuration
        );
    }

    private void UpdateStatusText(int charCount)
    {
        _displayedCharCount = charCount;
        if (_statusLabel != null && _targetStatusText.Length > 0)
        {
            string displayed = _targetStatusText[..Mathf.Min(charCount, _targetStatusText.Length)];
            if (charCount < _targetStatusText.Length)
                displayed += (Time.GetTicksMsec() / 100 % 2 == 0) ? "_" : " ";
            _statusLabel.Text = displayed;
        }
    }

    #endregion

    #region Grid Effects

    private void UpdateGridIntensity(float intensity)
    {
        _gridMaterial?.SetShaderParameter("intensity", intensity);
    }

    private async void FlashGrid(Color flashColor, float duration)
    {
        if (_gridMaterial == null) return;

        Color originalLarge = (Color)_gridMaterial.GetShaderParameter("grid_color_large");
        Color originalSmall = (Color)_gridMaterial.GetShaderParameter("grid_color_small");

        _gridMaterial.SetShaderParameter("grid_color_large", flashColor);
        _gridMaterial.SetShaderParameter("grid_color_small", flashColor * 0.7f);

        await ToSignal(GetTree().CreateTimer(duration), SceneTreeTimer.SignalName.Timeout);

        if (_gridMaterial != null)
        {
            _gridMaterial.SetShaderParameter("grid_color_large", originalLarge);
            _gridMaterial.SetShaderParameter("grid_color_small", originalSmall);
        }
    }

    #endregion
}