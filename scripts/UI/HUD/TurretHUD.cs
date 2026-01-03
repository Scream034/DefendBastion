#nullable enable

using Godot;
using Game.Turrets;
using Game.UI.Components;

namespace Game.UI.HUD;

/// <summary>
/// Контроллер HUD турели.
/// Отвечает за:
/// 1. Отображение состояния системы (температура, патроны, целостность).
/// 2. Визуализацию прицельной сетки и эффектов сканирования.
/// 3. Анимацию текстовых уведомлений.
/// </summary>
public partial class TurretHUD : Control
{
    #region Configuration: References

    [ExportGroup("References")]
    [Export] private AnimationPlayer _animPlayer = null!;
    [Export] private TurretReticle _reticle = null!;
    [Export] private ColorRect _gridOverlay = null!;
    [Export] private ZoomPixelationOverlay _pixelationOverlay = null!;

    [ExportSubgroup("Sensors & UI")]
    [Export] private SensorDataPanel _sensorPanel = null!;
    [Export] private Label _statusLabel = null!;
    [Export] private TemperatureSensorEmitter _tempSensor = null!;

    #endregion

    #region Configuration: Grid Visuals

    [ExportGroup("Visual Settings: Grid")]
    [Export(PropertyHint.Range, "0, 10")] private float _gridFadeSpeed = 5f;
    [Export(PropertyHint.Range, "0, 5")] private float _gridMinIntensity = 1.0f;
    [Export(PropertyHint.Range, "0, 10")] private float _gridMaxIntensity = 3.0f;

    [ExportSubgroup("Grid Colors")]
    // Базовые цвета сетки (для возврата после вспышек)
    [Export] private Color _gridColorDefaultLarge = new(0.1f, 0.4f, 0.4f, 0.3f);
    [Export] private Color _gridColorDefaultSmall = new(0.1f, 0.4f, 0.4f, 0.1f);

    // Цвета вспышек
    [Export] private Color _gridFlashColorNormal = new(1f, 0.95f, 0.8f, 0.4f);
    [Export] private Color _gridFlashColorCooling = new(0.4f, 0.7f, 1f, 0.3f);
    [Export] private Color _gridFlashColorCritical = new(1f, 0.2f, 0.1f, 0.5f);

    #endregion

    #region Configuration: Status & Colors

    [ExportGroup("Visual Settings: Status")]
    [Export] private float _statusFadeDuration = 0.15f;
    [Export] private float _statusTypeSpeed = 40f; // Символов в секунду
    [Export] private float _scannerMaxDistance = 1500f; // Лимит сканера (вместо LocalPlayer)

    [ExportSubgroup("Palette")]
    [Export] private Color _colorNormal = new(0.2f, 0.9f, 0.8f);     // Cyan
    [Export] private Color _colorWarning = new(1f, 0.7f, 0.2f);      // Orange
    [Export] private Color _colorCritical = new(1f, 0.3f, 0.2f);     // Red
    [Export] private Color _colorAction = new(1f, 0.95f, 0.8f);      // Off-White
    [Export] private Color _colorCooling = new(0.5f, 0.8f, 1f);      // Light Blue

    [ExportSubgroup("Localization")]
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

    // Константы ключей для панели сенсоров
    private const string KeyDist = "dist";
    private const string KeyAmmo = "ammo";
    private const string KeyTemp = "temp";
    private const string KeyHull = "hull";

    // Ссылки и кэш
    private ShaderMaterial? _gridMaterial;
    private PlayerControllableTurret? _turret;

    // Состояние анимаций
    private Tween? _statusTween;
    private Tween? _gridFlashTween;
    private string _targetStatusText = "";

    // Логика состояния
    private ShootingTurret.TurretState _lastState;
    private bool _wasOverheatWarning;
    private bool _wasCriticalOverheat;

    private PhysicsDirectSpaceState3D? _spaceState;
    private float _cachedDistance = -1f;

    // Интерполяция сетки
    private float _currentGridIntensity = 0f;
    private float _targetGridIntensity = 0f;

    #endregion

    #region Lifecycle


    public override void _Ready()
    {
        // По умолчанию HUD скрыт и не потребляет ресурсы
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);

        if (_gridOverlay != null)
        {
            _gridMaterial = _gridOverlay.Material as ShaderMaterial;
            // Устанавливаем дефолтные цвета сразу
            ResetGridColors();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_turret == null) return;

        // 1. Интерполяция интенсивности сетки (Lerp для плавности)
        _currentGridIntensity = Mathf.Lerp(_currentGridIntensity, _targetGridIntensity, (float)delta * _gridFadeSpeed);
        UpdateGridIntensity(_currentGridIntensity);

        // 2. Обновление данных сенсоров
        UpdateSensorData();

        // 3. Обновление пикселизации при зуме
        if (_reticle != null)
        {
            _pixelationOverlay?.SetIntensity(_reticle.PixelationIntensity, _reticle.CurrentZoom);
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Активирует HUD, подписывается на события турели и запускает анимацию загрузки.
    /// </summary>
    public void ShowHUD(PlayerControllableTurret turret)
    {
        _turret = turret;
        Visible = true;

        turret.GetTree().Connect(SceneTree.SignalName.PhysicsFrame, Callable.From(() => _spaceState = turret.GetWorld3D().DirectSpaceState), (uint)ConnectFlags.OneShot);

        ConfigureEnvironment();
        ConnectSignals();
        ResetState();
        InitializeVisuals();

        // Анимация старта
        AnimateStatus(_txtOnline, _colorNormal);
        _animPlayer?.Play(Constants.AnimPlayer_HUD_Boot);

        // Включаем обновление
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    /// <summary>
    /// Полностью отключает HUD, очищает твины и отписывается от событий.
    /// </summary>
    public void HideHUD()
    {
        KillTweens();
        DisconnectSignals();

        // Очистка дочерних компонентов
        _reticle?.Deinitialize();
        _tempSensor?.Deinitialize();
        _pixelationOverlay?.Reset();
        _sensorPanel?.Clear();

        _turret = null;
        Visible = false;
        _spaceState = null;

        // Отключаем цикл обновлений для экономии CPU
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    #endregion

    #region Initialization & Helpers

    private void ConfigureEnvironment()
    {
        // Настройка глобального логгера для эффекта погружения
        SharedHUD.SetLoggerPreset(LoggerPreset.FullLessLines);
        SharedHUD.SetLoggerVisible(true);
        RobotBus.Net("INIT: NEURAL_LINK_ESTABLISHED");
    }

    private void ConnectSignals()
    {
        if (_turret == null) return;

        // Ретикл (прицел)
        if (_reticle != null)
        {
            _reticle.Initialize(_turret);
            _reticle.OnAimingIntensityChanged += OnAimingIntensityChanged;
            _reticle.OnZoomChanged += OnZoomChanged;
        }

        // Датчик температуры
        if (_tempSensor != null)
        {
            _tempSensor.Initialize(_turret);
            _tempSensor.OnOverheatWarning += OnOverheatWarning;
            _tempSensor.OnCriticalOverheat += OnCriticalOverheat;
            _tempSensor.OnOverheatDamage += OnOverheatDamage;
        }

        // Сама турель
        _turret.OnStateChanged += OnTurretStateChanged;
        _turret.OnShot += OnTurretShot;
        _turret.OnAmmoChanged += OnAmmoChanged;

        _lastState = _turret.CurrentState;
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
        ResetGridColors(); // Сброс цветов сетки на дефолтные

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
        // Плавное изменение яркости сетки при прицеливании
        _targetGridIntensity = Mathf.Lerp(_gridMinIntensity, _gridMaxIntensity, intensity);
    }

    private void OnZoomChanged(float zoom, float pixelationIntensity)
    {
        _pixelationOverlay?.SetIntensity(pixelationIntensity, zoom);

        // Корректировка чувствительности мыши
        _turret?.CameraController.AdjustSensitivityByZoomLevel(zoom);

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

        // Визуальный глитч интерфейса при критическом перегреве
        SharedHUD.TriggerColoredGlitch(_colorWarning, 0.5f, 0.2f);
        RobotBus.Warn("THERMAL_CRITICAL: SYSTEM_DAMAGE_IMMINENT");
    }

    private void OnOverheatDamage(float damage)
    {
        // Периодический глитч при получении урона от температуры
        if (Time.GetTicksMsec() % 500 < 50)
        {
            SharedHUD.TriggerColoredGlitch(new Color(1f, 0.4f, 0.1f), 0.3f, 0.1f);
        }
    }

    private void OnTurretStateChanged(ShootingTurret.TurretState state)
    {
        if (state == _lastState) return;
        _lastState = state;

        // Сброс предупреждений, если состояние нормализовалось
        if (state == ShootingTurret.TurretState.Reloading)
        {
            _wasOverheatWarning = false;
            _wasCriticalOverheat = false;
        }

        // Обработка визуальных эффектов для разных состояний
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
                HandleIdleStateStatus();
                break;
        }
    }

    private void HandleIdleStateStatus()
    {
        if (_turret == null) return;

        if (_turret.CanShoot)
            AnimateStatus(_txtReady, _colorNormal);
        else if (!_turret.HasAmmoInMag)
            AnimateStatus(_txtNoAmmo, _colorCritical);
    }

    private void OnTurretShot()
    {
        RobotBus.Combat("SHOT_FIRED");
        // Легкая вспышка сетки при выстреле
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

    #region Visual Updates Implementation

    private void UpdateSensorData()
    {
        if (_sensorPanel == null || _turret == null) return;

        UpdateDistanceRaycast();
        UpdateTemperatureDisplay();
        UpdateIntegrityDisplay();
    }

    /// <summary>
    /// Выполняет физический луч от камеры турели для определения дистанции.
    /// Заменяет зависимость от игрока. Использует PhysicsDirectSpaceState.
    /// </summary>
    private void UpdateDistanceRaycast()
    {
        if (_turret == null || _spaceState == null) return;

        // Предполагаем, что у турели есть камера или точка выстрела
        var camera = _turret.CameraController?.GetCamera();
        if (camera == null) return;

        Vector3 origin = camera.GlobalPosition;
        Vector3 direction = -camera.GlobalTransform.Basis.Z; // Forward vector
        Vector3 target = origin + (direction * _scannerMaxDistance);

        var query = PhysicsRayQueryParameters3D.Create(origin, target);
        // Настроить маски коллизий если нужно: query.CollisionMask = ...
        // Исключаем саму турель из рейкаста
        var rid = _turret.GetRid();
        if (rid.IsValid)
        {
            query.Exclude = [rid];
        }

        var result = _spaceState.IntersectRay(query);

        float dist = 0f;
        bool hasHit = result.Count > 0;

        if (hasHit)
        {
            Vector3 hitPos = (Vector3)result["position"];
            dist = origin.DistanceTo(hitPos);
        }

        // Оптимизация: Обновляем текст только если значение существенно изменилось (hysteresis)
        if (!hasHit || dist > _scannerMaxDistance)
        {
            if (_cachedDistance != -1f) // Если ранее была дистанция
            {
                _sensorPanel.SetLine(KeyDist, "DIST", "----m");
                _cachedDistance = -1f;
            }
        }
        else if (Mathf.Abs(dist - _cachedDistance) > 1.0f) // Обновляем раз в 1 метр
        {
            _cachedDistance = dist;
            _sensorPanel.SetLine(KeyDist, "DIST", $"{dist:0000}m");
        }
    }

    private void UpdateTemperatureDisplay()
    {
        if (_sensorPanel == null || _tempSensor == null) return;

        float tempCelsius = _tempSensor.GetReadingCelsius();
        Color tempColor = _colorNormal;

        if (_tempSensor.IsOverheating)
        {
            // Мигание красным при перегреве
            float blink = (Mathf.Sin(Time.GetTicksMsec() / 80f) + 1f) * 0.5f;
            tempColor = _colorCritical.Lerp(new Color(_colorCritical, 0.1f), blink);
        }
        else if (_tempSensor.IsWarning)
        {
            tempColor = _colorWarning;
        }
        else
        {
            // Плавный градиент от нормального к предупреждающему
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
        Color color = _colorNormal;

        if (integrity <= 10f)
        {
            // Быстрое мигание при критическом уровне здоровья
            float blink = (Mathf.Sin(Time.GetTicksMsec() / 100f) + 1f) * 0.5f;
            color = _colorCritical.Lerp(new Color(_colorCritical, 0.1f), blink);
        }
        else if (integrity <= 30f)
        {
            color = _colorWarning;
        }

        _sensorPanel.SetLine(KeyHull, "HULL", $"{integrity:F0}%", color);
    }

    private void AnimateStatus(string text, Color color)
    {
        if (_statusLabel == null) return;

        _statusTween?.Kill();
        _statusTween = CreateTween();
        _statusTween.SetParallel(true);

        // Плавная смена цвета
        _statusTween.TweenProperty(_statusLabel, "modulate", color, _statusFadeDuration);

        _targetStatusText = text;

        // Эффект печатной машинки
        float typeDuration = text.Length / _statusTypeSpeed;
        _statusTween.TweenMethod(
            Callable.From<int>(UpdateStatusText),
            0, text.Length, typeDuration
        );
    }

    private void UpdateStatusText(int charCount)
    {
        if (_statusLabel == null || string.IsNullOrEmpty(_targetStatusText)) return;

        string displayed = _targetStatusText[..Mathf.Min(charCount, _targetStatusText.Length)];

        // Курсор, мигающий пока печатается текст
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
    /// Сбрасывает цвета сетки к значениям, заданным в инспекторе.
    /// </summary>
    private void ResetGridColors()
    {
        if (_gridMaterial == null) return;
        _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorLarge, _gridColorDefaultLarge);
        _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorSmall, _gridColorDefaultSmall);
    }

    /// <summary>
    /// Создает кратковременную вспышку сетки HUD с возвратом к дефолтным цветам.
    /// </summary>
    private void FlashGrid(Color flashColor, float duration)
    {
        if (_gridMaterial == null) return;

        // Установка цвета вспышки
        var targetLarge = flashColor;
        var targetSmall = flashColor * 0.7f; // Малая сетка чуть тусклее

        _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorLarge, targetLarge);
        _gridMaterial.SetShaderParameter(Constants.SP_GridOverlay_GridColorSmall, targetSmall);

        // Используем Tween для возврата к исходным цветам
        _gridFlashTween?.Kill();
        _gridFlashTween = CreateTween();

        _gridFlashTween.TweenInterval(duration);
        _gridFlashTween.TweenCallback(Callable.From(ResetGridColors));
    }

    #endregion
}