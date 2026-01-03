#nullable enable

using Godot;
using System.Collections.Generic;
using Game.Singletons; // Подключаем наши константы

namespace Game.UI.Components;

/// <summary>
/// Минималистичная панель для отображения сенсорных данных (текст + числовые значения).
/// Поддерживает плавную интерполяцию чисел и цветовую индикацию тревоги через шейдер.
/// </summary>
public partial class SensorDataPanel : Control
{
    #region Configuration

    [ExportGroup("Components")]
    [Export] private ColorRect _borderRect = null!;
    [Export] private VBoxContainer _dataContainer = null!;

    [ExportGroup("Appearance")]
    [Export] private Color _normalColor = new(0.2f, 0.9f, 0.8f, 1.0f);
    [Export] private Color _warningColor = new(1f, 0.7f, 0.2f, 1.0f);
    [Export] private Color _criticalColor = new(1f, 0.3f, 0.2f, 1.0f);

    [ExportGroup("Layout")]
    [Export] private int _fontSize = 13;
    [Export] private float _lineHeight = 18f;

    [ExportGroup("Animation")]
    [Export] private float _lerpSpeed = 8f;
    [Export] private float _flashDuration = 0.12f;

    #endregion

    #region Internal Types

    private class SensorLine
    {
        public Label Label = null!;
        public string LabelText = "";
        public float TargetValue;
        public float CurrentDisplayValue;
        public string LastFormat = "";
        public string LastSuffix = "";

        // Кэшируем цвет, чтобы не менять modualte каждый кадр без нужды
        public Color CurrentColor;
    }

    #endregion

    #region State

    private ShaderMaterial? _material;
    private readonly Dictionary<string, SensorLine> _lines = new();

    private float _alertLevel = 0f;
    private float _targetAlert = 0f;

    private Tween? _flashTween;

    #endregion

    #region Lifecycle

    public override void _Ready()
    {
        if (_borderRect != null)
        {
            _material = _borderRect.Material as ShaderMaterial;
            UpdateShaderSize();
            _borderRect.Resized += UpdateShaderSize;
        }

        // Событие самого контрола тоже полезно отслеживать
        Resized += UpdateShaderSize;
    }

    public override void _ExitTree()
    {
        Resized -= UpdateShaderSize;
        if (_borderRect != null)
            _borderRect.Resized -= UpdateShaderSize;

        _flashTween?.Kill();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        ProcessNumericInterpolation(dt);
        ProcessAlertLevel(dt);
    }

    #endregion

    #region Logic & Visuals

    private void ProcessNumericInterpolation(float dt)
    {
        // Проходим по линиям и обновляем только те, где значение изменилось
        foreach (var line in _lines.Values)
        {
            if (Mathf.Abs(line.CurrentDisplayValue - line.TargetValue) > 0.01f)
            {
                line.CurrentDisplayValue = Mathf.Lerp(line.CurrentDisplayValue, line.TargetValue, dt * _lerpSpeed);
                UpdateLabelText(line, line.LastFormat, line.LastSuffix);
            }
            else if (line.CurrentDisplayValue != line.TargetValue)
            {
                // Завершаем интерполяцию точным значением
                line.CurrentDisplayValue = line.TargetValue;
                UpdateLabelText(line, line.LastFormat, line.LastSuffix);
            }
        }
    }

    private void ProcessAlertLevel(float dt)
    {
        if (Mathf.Abs(_alertLevel - _targetAlert) > 0.001f)
        {
            _alertLevel = Mathf.Lerp(_alertLevel, _targetAlert, dt * 6f);
            _material?.SetShaderParameter(Constants.SP_GridOverlay_AlertLevel, _alertLevel);
        }
    }

    private void UpdateShaderSize()
    {
        if (_material == null || _borderRect == null) return;
        _material.SetShaderParameter(Constants.SP_GridOverlay_RectSize, _borderRect.Size);
    }

    private void UpdateLabelText(SensorLine line, string format, string suffix)
    {
        line.Label.Text = $"{line.LabelText}  {line.CurrentDisplayValue.ToString(format)}{suffix}";
    }

    #endregion

    #region Public API

    /// <summary>
    /// Устанавливает текстовую строку без числовой интерполяции.
    /// </summary>
    public void SetLine(string key, string labelText, string valueText, Color? color = null)
    {
        if (!_lines.TryGetValue(key, out var line))
        {
            line = CreateLine();
            _lines[key] = line;
        }

        // Обновляем текст только если он изменился (оптимизация рендеринга текста)
        string fullText = $"{labelText}  {valueText}";
        if (line.Label.Text != fullText)
        {
            line.Label.Text = fullText;
        }

        line.LabelText = labelText; // Сохраняем базу на всякий случай

        var targetColor = color ?? _normalColor;
        if (line.CurrentColor != targetColor)
        {
            line.Label.Modulate = targetColor;
            line.CurrentColor = targetColor;
        }
    }

    /// <summary>
    /// Устанавливает числовую строку с плавной интерполяцией значения.
    /// </summary>
    public void SetNumericLine(string key, string labelText, float value, string format = "F0", string suffix = "")
    {
        if (!_lines.TryGetValue(key, out var line))
        {
            line = CreateLine();
            _lines[key] = line;
        }

        line.LabelText = labelText;
        line.TargetValue = value;
        line.LastFormat = format;
        line.LastSuffix = suffix;

        // Если разница слишком большая ("скачок"), пропускаем интерполяцию для мгновенного обновления
        if (Mathf.Abs(line.CurrentDisplayValue - value) > Mathf.Max(10f, Mathf.Abs(value) * 0.5f))
        {
            line.CurrentDisplayValue = value;
            UpdateLabelText(line, format, suffix);
        }
        // Иначе интерполяция произойдет в _Process
    }

    /// <summary>
    /// Устанавливает уровень тревоги (влияет на шейдер рамки).
    /// </summary>
    /// <param name="level">От 0.0 до 1.0</param>
    public void SetAlertLevel(float level)
    {
        _targetAlert = Mathf.Clamp(level, 0f, 1f);
    }

    /// <summary>
    /// Вызывает цветовую вспышку рамки.
    /// </summary>
    public void Flash(Color? color = null)
    {
        if (_material == null) return;

        var flashColor = color ?? _normalColor;

        // Получаем текущие цвета или используем дефолт, если шейдер не инициализирован
        // ВАЖНО: Получение параметров из шейдера - тяжелая операция. 
        // Лучше хранить состояние цветов в C#, если возможно. 
        // Но для редкого эффекта Flash допустимо.
        var originalBorder = (Color)_material.GetShaderParameter(Constants.SP_GridOverlay_BorderColor);
        var originalCorner = (Color)_material.GetShaderParameter(Constants.SP_GridOverlay_CornerColor); // Предполагаем имя параметра

        // Перебиваем Tween'ом
        _flashTween?.Kill();
        _flashTween = CreateTween();

        // Мгновенно ставим цвет вспышки
        _material.SetShaderParameter(Constants.SP_GridOverlay_BorderColor, flashColor);
        _material.SetShaderParameter(Constants.SP_GridOverlay_CornerColor, flashColor);

        // Ждем и возвращаем обратно
        _flashTween.TweenInterval(_flashDuration);
        _flashTween.TweenCallback(Callable.From(() =>
        {
            // Проверка на null нужна, т.к. нода может быть удалена во время твина
            if (_material != null)
            {
                _material.SetShaderParameter(Constants.SP_GridOverlay_BorderColor, originalBorder);
                // Эффект "послесвечения" для уголков (чуть ярче оригинала)
                var brightCorner = new Color(originalBorder.R + 0.1f, originalBorder.G + 0.1f, originalBorder.B + 0.1f, originalBorder.A);
                _material.SetShaderParameter(Constants.SP_GridOverlay_CornerColor, brightCorner);
            }
        }));
    }

    public void RemoveLine(string key)
    {
        if (_lines.TryGetValue(key, out var line))
        {
            line.Label.QueueFree();
            _lines.Remove(key);
        }
    }

    public void Clear()
    {
        foreach (var line in _lines.Values)
        {
            line.Label.QueueFree();
        }
        _lines.Clear();
    }

    #endregion

    #region Helpers

    private SensorLine CreateLine()
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            // Используем CustomMinimumSize, чтобы строки не скакали по высоте
            CustomMinimumSize = new Vector2(0, _lineHeight)
        };

        label.AddThemeFontSizeOverride("font_size", _fontSize);
        _dataContainer?.AddChild(label);

        return new SensorLine
        {
            Label = label,
            CurrentColor = Colors.White // Начальное значение для отслеживания изменений
        };
    }

    #endregion
}