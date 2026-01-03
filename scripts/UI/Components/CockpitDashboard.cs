#nullable enable
using Godot;
using Game.Player;

namespace Game.UI.Components;

public partial class CockpitDashboard : Control
{
    [ExportGroup("Bindings")]
    [Export] public Label? CoordsLabel;
    [Export] public Label? TempLabel;

    [ExportGroup("Settings")]
    [Export] public float BaseTempKelvin { get; set; } = 310.0f;
    [Export] public float CriticalTempKelvin { get; set; } = 850.0f;
    [Export] public float NoiseAmplitude { get; set; } = 3.5f; // На сколько градусов может врать датчик

    private float _currentHealthPercent = 1.0f;
    private double _timeAccumulator = 0;
    private float _displayedTemp; // Для плавной интерполяции значений на экране

    public override void _Ready()
    {
        LocalPlayer.Instance.OnHealthChanged += OnHealthChanged;
        // Инициализируем сразу
        _displayedTemp = BaseTempKelvin;
    }

    public override void _ExitTree()
    {
        LocalPlayer.Instance.OnHealthChanged -= OnHealthChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        _timeAccumulator += delta;

        // Обновляем "шум" координат реже (как старый GPS)
        if (_timeAccumulator >= 0.1)
        {
            UpdateNoiseData();
            _timeAccumulator = 0;
        }

        // Температуру обновляем каждый кадр для плавности "аналогового" датчика
        UpdateTempDisplay((float)delta);
    }

    private void OnHealthChanged(float current)
    {
        _currentHealthPercent = Mathf.Clamp(current / LocalPlayer.Instance.MaxHealth, 0f, 1f);
    }

    private void UpdateTempDisplay(float dt)
    {
        if (TempLabel == null) return;

        // 1. Базовая целевая температура от здоровья
        float targetBase = Mathf.Lerp(CriticalTempKelvin, BaseTempKelvin, _currentHealthPercent);

        // 2. Генерация "живого" шума
        // Используем Time.GetTicksMsec() для получения времени
        float time = Time.GetTicksMsec() / 1000f;

        // Складываем две синусоиды с разной частотой, чтобы число не выглядело зацикленным
        // Одна медленная (дыхание системы), вторая быстрая (вибрации)
        float noise = (Mathf.Sin(time * 1.5f) + Mathf.Sin(time * 5.3f) * 0.3f) * NoiseAmplitude;

        // Если здоровья мало, амплитуда шума растет (система нестабильна)
        if (_currentHealthPercent < 0.4f) noise *= 3.0f;

        float targetWithNoise = targetBase + noise;

        // Плавная доводка цифр (эффект инерции датчика)
        _displayedTemp = Mathf.Lerp(_displayedTemp, targetWithNoise, dt * 5.0f);

        // 3. Цвет и отображение
        Color color;
        if (_currentHealthPercent > 0.6f) color = Colors.White;
        else if (_currentHealthPercent > 0.3f) color = new Color(1, 0.9f, 0.2f); // Желтоватый
        else
        {
            // При критическом состоянии добавляем мигание красным
            float flash = Mathf.Sin(time * 15f) > 0 ? 1f : 0.5f;
            color = new Color(1, 0.2f, 0.2f, flash);
        }

        // F0 - целые числа, выглядят более "технично" для старых дисплеев, чем дроби
        TempLabel.Text = $"CORE T: {_displayedTemp:F0} K";
        TempLabel.Modulate = color;
    }

    private void UpdateNoiseData()
    {
        if (CoordsLabel == null || LocalPlayer.Instance == null) return;

        var pos = LocalPlayer.Instance.GlobalPosition;

        // ... (твой код координат остался без изменений)
        double lat = pos.X + (GD.Randf() * 0.05f);
        double lon = pos.Z + (GD.Randf() * 0.05f);
        float windSpeed = 15f + (Mathf.Sin((float)Time.GetTicksMsec() / 1000f) * 2f);

        CoordsLabel.Text = $"POS: {lat:F2} : {lon:F2}\nATM: {windSpeed:F1} m/s [NW]";
    }
}