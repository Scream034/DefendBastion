#nullable enable

using Godot;

namespace Game.UI.Components;

/// <summary>
/// Полноэкранный глитч-эффект.
/// Использует шейдер для хроматической аберрации, смещения строк и шума.
/// </summary>
public partial class GlitchOverlay : ColorRect
{
    [ExportGroup("Timing")]
    [Export] public float DefaultDuration { get; set; } = 0.15f;
    [Export] public float AttackSpeed { get; set; } = 25.0f;  // Скорость нарастания
    [Export] public float DecaySpeed { get; set; } = 8.0f;    // Скорость затухания

    [ExportGroup("Intensity Presets")]
    [Export] public float ShotGlitchIntensity { get; set; } = 0.4f;
    [Export] public float HitGlitchIntensity { get; set; } = 0.7f;
    [Export] public float CriticalGlitchIntensity { get; set; } = 1.0f;

    private ShaderMaterial? _material;
    private float _currentIntensity = 0f;
    private float _targetIntensity = 0f;
    private float _glitchTimer = 0f;
    private Color _currentTint = Colors.White;
    private Color _targetTint = Colors.White;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        
        _material = Material as ShaderMaterial;
        
        Visible = false;
        UpdateShader();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        
        if (_glitchTimer > 0)
        {
            _glitchTimer -= dt;
            _currentIntensity = Mathf.Lerp(_currentIntensity, _targetIntensity, dt * AttackSpeed);
            _currentTint = _currentTint.Lerp(_targetTint, dt * AttackSpeed);
        }
        else
        {
            _currentIntensity = Mathf.Lerp(_currentIntensity, 0f, dt * DecaySpeed);
            _currentTint = _currentTint.Lerp(Colors.White, dt * DecaySpeed);
        }
        
        Visible = _currentIntensity > 0.005f;
        
        if (Visible)
            UpdateShader();
    }

    /// <summary>
    /// Запуск глитч-эффекта.
    /// </summary>
    public void Trigger(float intensity = 1.0f, float duration = -1f)
    {
        _targetIntensity = Mathf.Max(_targetIntensity, intensity);
        _currentIntensity = Mathf.Max(_currentIntensity, intensity * 0.3f); // Мгновенный старт
        _glitchTimer = duration > 0 ? duration : DefaultDuration;
    }

    /// <summary>
    /// Глитч с цветовым оттенком.
    /// </summary>
    public void TriggerColored(Color tint, float intensity = 1.0f, float duration = -1f)
    {
        _targetTint = tint;
        Trigger(intensity, duration);
    }

    /// <summary>
    /// Пресет для выстрела.
    /// </summary>
    public void TriggerShot()
    {
        TriggerColored(new Color(1f, 0.95f, 0.8f), ShotGlitchIntensity, 0.08f);
    }

    /// <summary>
    /// Пресет для получения урона.
    /// </summary>
    public void TriggerHit()
    {
        TriggerColored(new Color(1f, 0.3f, 0.2f), HitGlitchIntensity, 0.2f);
    }

    private void UpdateShader()
    {
        if (_material == null) return;
        
        _material.SetShaderParameter("glitch_intensity", _currentIntensity);
        _material.SetShaderParameter("color_tint", _currentTint);
        _material.SetShaderParameter("time", Time.GetTicksMsec() / 1000f);
    }
}