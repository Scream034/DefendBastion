#nullable enable

using Godot;

namespace Game.UI.Components;

/// <summary>
/// Оверлей пикселизации для эффекта зума турели.
/// </summary>
public partial class ZoomPixelationOverlay : ColorRect
{
    [ExportGroup("Settings")]
    [Export] public float LerpSpeed { get; set; } = 5.5f;
    [Export] public float MinPixelSize { get; set; } = 0.1f;
    [Export] public float MaxPixelSize { get; set; } = 1.2f;

    private ShaderMaterial? _material;
    private float _currentIntensity = 0f;
    private float _targetIntensity = 0f;
    private float _currentZoom = 1f;

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
        
        _currentIntensity = Mathf.Lerp(_currentIntensity, _targetIntensity, dt * LerpSpeed);
        
        // Показываем только при заметной пикселизации
        Visible = _currentIntensity > 0.01f;
        
        if (Visible)
            UpdateShader();
    }

    /// <summary>
    /// Установить интенсивность пикселизации.
    /// </summary>
    public void SetIntensity(float intensity, float zoomLevel = 1f)
    {
        _targetIntensity = Mathf.Clamp(intensity, 0f, 1f);
        _currentZoom = zoomLevel;
    }

    /// <summary>
    /// Мгновенно сбросить пикселизацию.
    /// </summary>
    public void Reset()
    {
        _targetIntensity = 0f;
        _currentIntensity = 0f;
        _currentZoom = 1f;
        Visible = false;
    }

    private void UpdateShader()
    {
        if (_material == null) return;
        
        _material.SetShaderParameter("pixelation_intensity", _currentIntensity);
        _material.SetShaderParameter("zoom_level", _currentZoom);
        _material.SetShaderParameter("min_pixel_size", MinPixelSize);
        _material.SetShaderParameter("max_pixel_size", MaxPixelSize);
    }
}