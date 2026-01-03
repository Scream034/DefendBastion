#nullable enable

using Godot;
using Game.Player;

namespace Game.UI.Components;

public partial class CockpitDashboard : Control
{
    [ExportGroup("Components")]
    [Export] private Label _coordsLabel = null!;
    [Export] private Label _tempLabel = null!;
    [Export] private TemperatureSensorEmitter _tempSensor = null!;

    private double _coordUpdateTimer = 0;

    public override void _Ready()
    {
        // Убеждаемся что сенсор в правильном режиме
        if (_tempSensor != null)
        {
            _tempSensor.Mode = TemperatureMode.DamageBasedHeating;
        }
        
        if (LocalPlayer.Instance != null)
        {
            LocalPlayer.Instance.OnHealthChanged += OnHealthChanged;
        }
    }

    public override void _ExitTree()
    {
        if (LocalPlayer.Instance != null)
        {
            LocalPlayer.Instance.OnHealthChanged -= OnHealthChanged;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateCoordinates(delta);
        UpdateTemperatureDisplay();
    }

    private void OnHealthChanged(float current)
    {
        UpdateSensorState();
    }

    private void UpdateSensorState()
    {
        if (LocalPlayer.Instance == null || _tempSensor == null) return;

        float healthPct = Mathf.Clamp(
            LocalPlayer.Instance.Health / LocalPlayer.Instance.MaxHealth, 
            0f, 1f
        );

        // Определяем, под нагрузкой ли игрок (бег, прыжок и т.д.)
        bool isUnderLoad = !LocalPlayer.Instance.IsAlive; // Если есть такое свойство
        
        _tempSensor.UpdateState(healthPct, isUnderLoad);
    }

    private void UpdateTemperatureDisplay()
    {
        if (_tempLabel == null || _tempSensor == null) return;

        float kelvin = _tempSensor.GetReading();

        // Цвет на основе состояния сенсора
        Color color;
        
        if (_tempSensor.IsOverheating)
        {
            // Мигание при критической температуре
            float flash = Mathf.Sin(Time.GetTicksMsec() / 50f) > 0 ? 1f : 0.4f;
            color = new Color(1f, 0.2f, 0.2f, flash);
        }
        else if (_tempSensor.IsWarning)
        {
            color = new Color(1f, 0.7f, 0.2f);
        }
        else
        {
            // Градиент от нормального к желтоватому
            float t = _tempSensor.NormalizedTemperature;
            color = new Color(
                Mathf.Lerp(0.8f, 1f, t),
                Mathf.Lerp(0.9f, 0.8f, t),
                Mathf.Lerp(0.9f, 0.3f, t)
            );
        }

        _tempLabel.Text = $"CORE T: {kelvin:F0} K";
        _tempLabel.Modulate = color;
    }

    private void UpdateCoordinates(double delta)
    {
        if (_coordsLabel == null || LocalPlayer.Instance == null) return;

        _coordUpdateTimer += delta;
        if (_coordUpdateTimer < 0.1) return;
        _coordUpdateTimer = 0;

        var pos = LocalPlayer.Instance.GlobalPosition;

        double lat = pos.X + GD.RandRange(-0.05, 0.05);
        double lon = pos.Z + GD.RandRange(-0.05, 0.05);

        float time = Time.GetTicksMsec() / 1000f;
        float windSpeed = 15f + (Mathf.Sin(time) * 2f);

        _coordsLabel.Text = $"POS: {lat:F2} : {lon:F2}\nATM: {windSpeed:F1} m/s [NW]";
    }
}