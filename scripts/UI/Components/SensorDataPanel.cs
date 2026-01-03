#nullable enable

using Godot;
using System.Collections.Generic;

namespace Game.UI.Components;

/// <summary>
/// Минималистичная панель сенсорных данных.
/// </summary>
public partial class SensorDataPanel : Control
{
    [ExportGroup("Components")]
    [Export] private ColorRect _borderRect = null!;
    [Export] private VBoxContainer _dataContainer = null!;

    [ExportGroup("Colors")]
    [Export] private Color _normalColor = new(0.2f, 0.9f, 0.8f, 1.0f);
    [Export] private Color _warningColor = new(1f, 0.7f, 0.2f, 1.0f);
    [Export] private Color _criticalColor = new(1f, 0.3f, 0.2f, 1.0f);

    [ExportGroup("Layout")]
    [Export] private int _fontSize = 13;
    [Export] private float _lineHeight = 18f;

    [ExportGroup("Animation")]
    [Export] private float _lerpSpeed = 8f;

    private ShaderMaterial? _material;
    private readonly Dictionary<string, SensorLine> _lines = new();
    private float _alertLevel = 0f;
    private float _targetAlert = 0f;

    private class SensorLine
    {
        public Label Label = null!;
        public string Key = "";
        public float Value = 0f;
        public float DisplayValue = 0f;
    }

    public override void _Ready()
    {
        if (_borderRect != null)
        {
            _material = _borderRect.Material as ShaderMaterial;
            UpdateShaderSize();
        }

        Resized += UpdateShaderSize;
        if (_borderRect != null)
            _borderRect.Resized += UpdateShaderSize;
    }

    public override void _ExitTree()
    {
        Resized -= UpdateShaderSize;
        if (_borderRect != null)
            _borderRect.Resized -= UpdateShaderSize;
    }

    private void UpdateShaderSize()
    {
        if (_material == null || _borderRect == null) return;
        _material.SetShaderParameter("rect_size", _borderRect.Size);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Плавное изменение чисел
        foreach (var line in _lines.Values)
        {
            if (Mathf.Abs(line.DisplayValue - line.Value) > 0.1f)
            {
                line.DisplayValue = Mathf.Lerp(line.DisplayValue, line.Value, dt * _lerpSpeed);
            }
        }

        // Плавное изменение тревоги
        _alertLevel = Mathf.Lerp(_alertLevel, _targetAlert, dt * 6f);
        _material?.SetShaderParameter("alert_level", _alertLevel);
    }

    public void SetLine(string key, string label, string value, Color? color = null)
    {
        if (!_lines.TryGetValue(key, out var line))
        {
            line = CreateLine(key);
            _lines[key] = line;
        }

        line.Key = label;
        line.Label.Text = $"{label}  {value}";
        line.Label.Modulate = color ?? _normalColor;
    }

    public void SetNumericLine(string key, string label, float value, string format = "F0", string suffix = "")
    {
        if (!_lines.TryGetValue(key, out var line))
        {
            line = CreateLine(key);
            _lines[key] = line;
        }

        line.Key = label;
        line.Value = value;

        if (Mathf.Abs(line.DisplayValue - value) > Mathf.Max(10f, Mathf.Abs(value) * 0.3f))
        {
            line.DisplayValue = value;
        }

        line.Label.Text = $"{label}  {line.DisplayValue.ToString(format)}{suffix}";
    }

    public void SetAlertLevel(float level)
    {
        _targetAlert = Mathf.Clamp(level, 0f, 1f);
    }

    public void Flash(Color? color = null)
    {
        if (_material == null) return;

        var flash = color ?? _normalColor;
        var original = (Color)_material.GetShaderParameter("border_color");

        _material.SetShaderParameter("border_color", flash);
        _material.SetShaderParameter("corner_accent_color", flash);

        GetTree().CreateTimer(0.12f).Timeout += () =>
        {
            _material?.SetShaderParameter("border_color", original);
            _material?.SetShaderParameter("corner_accent_color",
                new Color(original.R + 0.1f, original.G + 0.1f, original.B + 0.1f, 1f));
        };
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
            line.Label.QueueFree();
        _lines.Clear();
    }

    private SensorLine CreateLine(string key)
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(0, _lineHeight)
        };

        label.AddThemeFontSizeOverride("font_size", _fontSize);
        _dataContainer?.AddChild(label);

        return new SensorLine { Label = label };
    }
}