#nullable enable

using Godot;
using Game.Turrets;
using System;

namespace Game.UI.Components;

public enum TemperatureMode
{
    /// <summary>
    /// Температура зависит от повреждений.
    /// Полное HP = низкая температура, 0 HP = высокая.
    /// </summary>
    DamageBasedHeating,
    
    /// <summary>
    /// Температура от стрельбы (для турели).
    /// </summary>
    LoadBasedHeating
}

[GlobalClass]
public sealed partial class TemperatureSensorEmitter : Node
{
    [ExportGroup("Mode")]
    [Export] public TemperatureMode Mode { get; set; } = TemperatureMode.DamageBasedHeating;

    [ExportGroup("Temperature Range (Kelvin)")]
    [Export] public float AmbientTempKelvin { get; set; } = 293.0f;     // 20°C
    [Export] public float IdleTempKelvin { get; set; } = 310.0f;        // 37°C
    [Export] public float WorkTempKelvin { get; set; } = 450.0f;        // 177°C
    [Export] public float WarningTempKelvin { get; set; } = 550.0f;     // 277°C
    [Export] public float CriticalTempKelvin { get; set; } = 700.0f;    // 427°C
    [Export] public float MaxTempKelvin { get; set; } = 850.0f;         // 577°C

    [ExportGroup("Damage-Based Heating")]
    [Export] public float HealthyTemp { get; set; } = 310.0f;           // 37°C при 100% HP
    [Export] public float CriticalDamageTemp { get; set; } = 720.0f;    // При 0% HP
    [Export] public float TempChangeSpeed { get; set; } = 1.5f;         // Скорость изменения

    [ExportGroup("Load-Based Heating")]
    [Export] public float HeatPerShot { get; set; } = 15.0f;
    [Export] public float PassiveHeatingRate { get; set; } = 5.0f;
    [Export] public float CoolingRate { get; set; } = 8.0f;
    [Export] public float ActiveCoolingRate { get; set; } = 25.0f;
    [Export] public float DamagedCoolingPenalty { get; set; } = 0.5f;

    [ExportGroup("Overheat Damage")]
    [Export] public bool EnableOverheatDamage { get; set; } = false;
    [Export] public float OverheatDamageRate { get; set; } = 2.0f;

    [ExportGroup("Signal Noise")]
    [Export] public float NoiseAmplitude { get; set; } = 2.0f;
    [Export] public float NoiseFrequency { get; set; } = 1.5f;
    [Export] public float DamagedNoiseMultiplier { get; set; } = 3.0f;

    private float _internalTemp;
    private float _targetTemp;
    private double _timeAccumulator;
    private float _currentIntegrity = 1.0f;
    private bool _isUnderLoad = false;
    private bool _isActiveCooling = false;
    
    private PlayerControllableTurret? _turretTarget;

    public event Action<float>? OnTemperatureChanged;
    public event Action? OnOverheatWarning;
    public event Action? OnCriticalOverheat;
    public event Action<float>? OnOverheatDamage;

    public float CurrentTemperature => _internalTemp;
    public float CurrentTemperatureCelsius => _internalTemp - 273.15f;
    public float NormalizedTemperature => Mathf.Clamp(
        (_internalTemp - AmbientTempKelvin) / (MaxTempKelvin - AmbientTempKelvin), 0f, 1f);
    public bool IsOverheating => _internalTemp >= CriticalTempKelvin;
    public bool IsWarning => _internalTemp >= WarningTempKelvin;

    public override void _Ready()
    {
        // Начинаем с температуры "здоровой системы"
        _internalTemp = HealthyTemp;
        _targetTemp = HealthyTemp;
    }

    public void Initialize(PlayerControllableTurret turret)
    {
        Deinitialize();

        _turretTarget = turret;
        Mode = TemperatureMode.LoadBasedHeating;
        EnableOverheatDamage = true;
        _internalTemp = IdleTempKelvin;
        _targetTemp = IdleTempKelvin;

        if (_turretTarget != null)
        {
            _turretTarget.OnShot += OnTurretShot;
            _turretTarget.OnStateChanged += OnTurretStateChanged;
            UpdateIntegrityFromTurret();
        }
    }

    public void Deinitialize()
    {
        if (_turretTarget != null)
        {
            _turretTarget.OnShot -= OnTurretShot;
            _turretTarget.OnStateChanged -= OnTurretStateChanged;
            _turretTarget = null;
        }
    }

    /// <summary>
    /// Обновить состояние (для игрока/машины).
    /// </summary>
    /// <param name="integrity">1.0 = полное HP (холодно), 0.0 = мёртв (горячо)</param>
    public void UpdateState(float integrity, bool isUnderLoad, bool isActiveCooling = false)
    {
        _currentIntegrity = Mathf.Clamp(integrity, 0f, 1f);
        _isUnderLoad = isUnderLoad;
        _isActiveCooling = isActiveCooling;
    }

    public void AddHeat(float kelvin)
    {
        _internalTemp = Mathf.Min(_internalTemp + kelvin, MaxTempKelvin);
    }

    public void ForceCool(float kelvin)
    {
        _internalTemp = Mathf.Max(_internalTemp - kelvin, AmbientTempKelvin);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _timeAccumulator += delta;

        if (_turretTarget != null && IsInstanceValid(_turretTarget))
        {
            UpdateIntegrityFromTurret();
            _isUnderLoad = _turretTarget.CurrentState == ShootingTurret.TurretState.Shooting;
            _isActiveCooling = _turretTarget.CurrentState == ShootingTurret.TurretState.Reloading;
        }

        float previousTemp = _internalTemp;

        if (Mode == TemperatureMode.DamageBasedHeating)
        {
            ProcessDamageBasedHeating(dt);
        }
        else
        {
            ProcessLoadBasedHeating(dt);
        }

        _internalTemp = Mathf.Clamp(_internalTemp, AmbientTempKelvin, MaxTempKelvin);

        // События
        if (Mathf.Abs(_internalTemp - previousTemp) > 0.5f)
            OnTemperatureChanged?.Invoke(_internalTemp);

        if (previousTemp < WarningTempKelvin && _internalTemp >= WarningTempKelvin)
            OnOverheatWarning?.Invoke();
        
        if (previousTemp < CriticalTempKelvin && _internalTemp >= CriticalTempKelvin)
            OnCriticalOverheat?.Invoke();
    }

    private void ProcessDamageBasedHeating(float dt)
    {
        // КЛЮЧЕВАЯ ФОРМУЛА:
        // integrity = 1.0 (100% HP) → HealthyTemp (310K, холодно)
        // integrity = 0.0 (0% HP)   → CriticalDamageTemp (720K, горячо)
        //
        // Lerp(a, b, t) = a + (b - a) * t
        // Lerp(HealthyTemp, CriticalDamageTemp, 1 - integrity)
        //
        // При integrity = 1.0: Lerp(310, 720, 0.0) = 310 ✓
        // При integrity = 0.0: Lerp(310, 720, 1.0) = 720 ✓
        
        float damageRatio = 1.0f - _currentIntegrity;  // 0 при полном HP, 1 при 0 HP
        _targetTemp = Mathf.Lerp(HealthyTemp, CriticalDamageTemp, damageRatio);
        
        // Нагрузка добавляет тепло
        if (_isUnderLoad)
        {
            _targetTemp += 50.0f;
        }
        
        // Плавное изменение температуры
        _internalTemp = Mathf.Lerp(_internalTemp, _targetTemp, dt * TempChangeSpeed);
    }

    private void ProcessLoadBasedHeating(float dt)
    {
        if (_isUnderLoad)
        {
            _internalTemp += PassiveHeatingRate * dt;
        }

        float targetTemp = _isUnderLoad ? WorkTempKelvin : 
                          _isActiveCooling ? AmbientTempKelvin : 
                          IdleTempKelvin;

        if (_internalTemp > targetTemp)
        {
            float coolingSpeed = _isActiveCooling ? ActiveCoolingRate : CoolingRate;
            
            if (_currentIntegrity < 1.0f)
            {
                coolingSpeed *= Mathf.Lerp(DamagedCoolingPenalty, 1.0f, _currentIntegrity);
            }
            
            _internalTemp = Mathf.MoveToward(_internalTemp, targetTemp, coolingSpeed * dt);
        }
    }

    public float GetReading()
    {
        return _internalTemp + CalculateNoise();
    }

    public float GetReadingCelsius()
    {
        return GetReading() - 273.15f;
    }

    private void OnTurretShot() => AddHeat(HeatPerShot);

    private void OnTurretStateChanged(ShootingTurret.TurretState state)
    {
        _isUnderLoad = state == ShootingTurret.TurretState.Shooting;
        _isActiveCooling = state == ShootingTurret.TurretState.Reloading;
    }

    private void UpdateIntegrityFromTurret()
    {
        if (_turretTarget == null) return;
        float maxHp = _turretTarget.MaxHealth;
        if (maxHp > 0)
            _currentIntegrity = _turretTarget.Health / maxHp;
    }

    private float CalculateNoise()
    {
        float t = (float)_timeAccumulator * NoiseFrequency;
        float noise = (Mathf.Sin(t * 1.2f) * 0.7f + Mathf.Sin(t * 4.7f) * 0.3f) * NoiseAmplitude;

        if (_currentIntegrity < 0.5f)
        {
            float factor = 1.0f + (1.0f - _currentIntegrity * 2f) * (DamagedNoiseMultiplier - 1f);
            noise *= factor;
        }

        if (_isUnderLoad)
        {
            noise += Mathf.Sin(t * 25f) * NoiseAmplitude * 0.5f;
        }

        return noise;
    }

    public override void _ExitTree() => Deinitialize();
}