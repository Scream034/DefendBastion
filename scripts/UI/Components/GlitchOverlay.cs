#nullable enable

using Godot;

namespace Game.UI.Components;

/// <summary>
/// Управляет полноэкранным эффектом глитча (помех).
/// Реализует логику атаки (Attack) и затухания (Decay) интенсивности эффекта.
/// </summary>
public partial class GlitchOverlay : ColorRect
{
    #region Configuration

    [ExportGroup("Timing")]
    [Export] public float DefaultDuration { get; set; } = 0.15f;
    [Export] public float AttackSpeed { get; set; } = 25.0f;  // Скорость нарастания
    [Export] public float DecaySpeed { get; set; } = 8.0f;    // Скорость затухания

    [ExportGroup("Intensity Presets")]
    [Export] public float ShotGlitchIntensity { get; set; } = 0.4f;
    [Export] public float HitGlitchIntensity { get; set; } = 0.7f;
    [Export] public float CriticalGlitchIntensity { get; set; } = 1.0f;

    #endregion

    #region State

    private ShaderMaterial? _material;

    private float _currentIntensity = 0f;
    private float _targetIntensity = 0f;
    private float _glitchTimer = 0f;

    // Используем аккумулятор времени для плавности, вместо GetTicksMsec
    private float _shaderTimeAccumulator = 0f;

    private Color _currentTint = Colors.White;
    private Color _targetTint = Colors.White;

    #endregion

    #region Lifecycle

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _material = Material as ShaderMaterial;

#if DEBUG
        if (_material == null)
        {
            GD.PushWarning($"[{Name}] ShaderMaterial не назначен. Эффекты работать не будут.");
            SetProcess(false);
            return;
        }
#endif

        // Скрываем и отключаем процессинг, пока эффект не нужен
        Visible = false;
        SetProcess(false);
        UpdateShaderParams();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _shaderTimeAccumulator += dt;

        UpdateIntensity(dt);

        // Если эффект затух, отключаем отрисовку и процессинг для экономии ресурсов
        if (_currentIntensity <= 0.005f && _glitchTimer <= 0)
        {
            _currentIntensity = 0f;
            Visible = false;
            SetProcess(false);
            return;
        }

        if (!Visible) Visible = true;
        UpdateShaderParams();
    }

    #endregion

    #region Logic

    private void UpdateIntensity(float dt)
    {
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
    }

    private void UpdateShaderParams()
    {
        if (_material == null) return;

        _material.SetShaderParameter(Constants.SP_GlitchOverlay_Intensity, _currentIntensity);
        _material.SetShaderParameter(Constants.SP_GlitchOverlay_Tint, _currentTint);
        _material.SetShaderParameter(Constants.SP_GlitchOverlay_Time, _shaderTimeAccumulator);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Запуск глитч-эффекта с базовым белым тинтом.
    /// </summary>
    /// <param name="intensity">Сила эффекта (0.0 - 1.0+).</param>
    /// <param name="duration">Длительность в секундах. Если -1, используется DefaultDuration.</param>
    public void Trigger(float intensity = 1.0f, float duration = -1f)
    {
        // Сбрасываем тинт в белый при обычном триггере
        TriggerColored(Colors.White, intensity, duration);
    }

    /// <summary>
    /// Запуск глитч-эффекта с определенным цветовым оттенком.
    /// </summary>
    public void TriggerColored(Color tint, float intensity = 1.0f, float duration = -1f)
    {
        if (_material == null) return;

        // Включаем процессинг, если он был выключен
        if (!IsProcessing()) SetProcess(true);

        _targetTint = tint;
        _targetIntensity = Mathf.Max(_targetIntensity, intensity);

        // Мгновенный рывок интенсивности для резкого начала (Attack)
        _currentIntensity = Mathf.Max(_currentIntensity, intensity * 0.4f);

        _glitchTimer = duration > 0 ? duration : DefaultDuration;
    }

    /// <summary>
    /// Пресет: визуальная отдача при выстреле (теплый оттенок).
    /// </summary>
    public void TriggerShot()
    {
        TriggerColored(new Color(1f, 0.95f, 0.8f), ShotGlitchIntensity, 0.08f);
    }

    /// <summary>
    /// Пресет: получение урона (красный оттенок).
    /// </summary>
    public void TriggerHit()
    {
        TriggerColored(new Color(1f, 0.3f, 0.2f), HitGlitchIntensity, 0.2f);
    }

    /// <summary>
    /// Пресет: критическое состояние (максимальная интенсивность).
    /// </summary>
    public void TriggerCritical()
    {
        TriggerColored(new Color(1f, 0.1f, 0.1f), CriticalGlitchIntensity, 0.3f);
    }

    #endregion
}