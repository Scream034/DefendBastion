#nullable enable

using Godot;
using Game.UI.Components;

namespace Game.UI.HUD;

/// <summary>
/// Глобальный HUD-слой, содержащий компоненты, которые видны
/// независимо от активного режима (игрок, турель, транспорт).
/// </summary>
[GlobalClass]
public partial class SharedHUD : Control
{
    public static SharedHUD? Instance { get; private set; }

    [ExportGroup("Core Components")]
    [Export] public DataLogger Logger { get; private set; } = null!;
    [Export] public GlitchOverlay Glitch { get; private set; } = null!;

    [ExportGroup("Settings")]
    [Export] public bool LoggerVisibleByDefault { get; set; } = true;

    public override void _EnterTree()
    {
        Instance = this;
        ZIndex = 100; // Поверх других CanvasLayer
    }

    public override void _Ready()
    {
        if (Logger != null)
        {
            Logger.Visible = LoggerVisibleByDefault;
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    #region Static API

    /// <summary>
    /// Вызывает глитч-эффект на экране.
    /// </summary>
    public static void TriggerGlitch(float intensity = 1.0f, float duration = 0.15f)
    {
        Instance?.Glitch?.Trigger(intensity, duration);
    }

    /// <summary>
    /// Вызывает глитч с цветовым оттенком (для разных типов событий).
    /// </summary>
    public static void TriggerColoredGlitch(Color tint, float intensity = 1.0f, float duration = 0.15f)
    {
        Instance?.Glitch?.TriggerColored(tint, intensity, duration);
    }

    /// <summary>
    /// Быстрый доступ к логгеру.
    /// </summary>
    public static void Log(LogChannel channel, string message)
    {
        RobotBus.Log(channel, message);
    }

    /// <summary>
    /// Показать/скрыть логгер.
    /// </summary>
    public static void SetLoggerVisible(bool visible)
    {
        if (Instance?.Logger != null)
            Instance.Logger.Visible = visible;
    }

    /// <summary>
    /// Переместить логгер в указанную позицию (для разных режимов HUD).
    /// </summary>
    public static void SetLoggerPosition(Vector2 position, Vector2? size = null)
    {
        if (Instance?.Logger != null)
        {
            Instance.Logger.Position = position;
            if (size.HasValue)
                Instance.Logger.Size = size.Value;
        }
    }

    /// <summary>
    /// Пресеты позиций логгера для разных режимов.
    /// </summary>
    public static void SetLoggerPreset(LoggerPreset preset)
    {
        if (Instance?.Logger == null) return;

        static void fullPreset()
        {
            Instance!.Logger.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            Instance.Logger.Position = new Vector2(10, 10);
            Instance.Logger.Size = new Vector2(500, 300);
        }

        switch (preset)
        {
            case LoggerPreset.FullLessLines:
                fullPreset();
                Instance.Logger.VisibleLines = Instance.Logger.MaxLines / 2;
                break;

            case LoggerPreset.Full:
                fullPreset();
                Instance.Logger.VisibleLines = Instance.Logger.MaxLines;
                break;
        }
    }

    #endregion
}

public enum LoggerPreset
{
    FullLessLines,
    Full,
}