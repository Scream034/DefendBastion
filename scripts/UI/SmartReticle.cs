#nullable enable

using Game.Player;
using Godot;

namespace Game.UI;

public partial class SmartReticle : Control
{
    [ExportGroup("Stabilization")]
    [Export] public float BaseSize { get; set; } = 6.0f;
    [Export] public float MaxSize { get; set; } = 24.0f;
    [Export] public float StabilizeSpeed { get; set; } = 2.5f;
    [Export] public float DestabilizeSpeed { get; set; } = 8.0f;

    [ExportGroup("Visuals")]
    [Export] public Color IdleColor { get; set; } = new Color("#00aaff");
    [Export] public Color LockColor { get; set; } = new Color("#ff3333");
    [Export] public float LineThickness { get; set; } = 0.8f;

    [ExportGroup("Center Dot Behavior")]
    [Export] public float DotMaxSize { get; set; } = 1.6f;  // Размер в упор
    [Export] public float DotMinSize { get; set; } = 0.4f;  // Размер вдалеке
    [Export] public float FadeDistanceStart { get; set; } = 30.0f; // Начало исчезновения (метры)
    [Export] public float FadeDistanceEnd { get; set; } = 4.0f;   // Полное исчезновение (метры)
    [Export] public float MaxEffectDistance { get; set; } = 400.0f; // Дистанция, где точка становится минимальной

    private float _currentSize;
    private bool _isLocked = false;
    private string _targetName = "";

    // По умолчанию ставим дистанцию побольше, чтобы точка была видна при старте
    private float _targetDistance = 50f;

    public override void _Ready()
    {
        _currentSize = MaxSize;
        RobotBus.OnTargetScannerUpdate += HandleScanData;
    }

    public override void _ExitTree()
    {
        RobotBus.OnTargetScannerUpdate -= HandleScanData;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool isMoving = LocalPlayer.Instance.Velocity.Length() > LocalPlayer.Instance.Speed / 2f;

        float targetSize = isMoving ? MaxSize : BaseSize;
        float speed = isMoving ? DestabilizeSpeed : StabilizeSpeed;

        _currentSize = Mathf.Lerp(_currentSize, targetSize, dt * speed);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 center = Size / 2;
        float halfSize = _currentSize / 2;
        Color color = _isLocked ? LockColor : IdleColor;

        // --- Рисуем уголки (код тот же) ---
        float cornerLen = _currentSize * 0.3f;
        // Верхний левый
        DrawLine(center + new Vector2(-halfSize, -halfSize), center + new Vector2(-halfSize + cornerLen, -halfSize), color, LineThickness);
        DrawLine(center + new Vector2(-halfSize, -halfSize), center + new Vector2(-halfSize, -halfSize + cornerLen), color, LineThickness);
        // Верхний правый
        DrawLine(center + new Vector2(halfSize, -halfSize), center + new Vector2(halfSize - cornerLen, -halfSize), color, LineThickness);
        DrawLine(center + new Vector2(halfSize, -halfSize), center + new Vector2(halfSize, -halfSize + cornerLen), color, LineThickness);
        // Нижний левый
        DrawLine(center + new Vector2(-halfSize, halfSize), center + new Vector2(-halfSize + cornerLen, halfSize), color, LineThickness);
        DrawLine(center + new Vector2(-halfSize, halfSize), center + new Vector2(-halfSize, halfSize - cornerLen), color, LineThickness);
        // Нижний правый
        DrawLine(center + new Vector2(halfSize, halfSize), center + new Vector2(halfSize - cornerLen, halfSize), color, LineThickness);
        DrawLine(center + new Vector2(halfSize, halfSize), center + new Vector2(halfSize, halfSize - cornerLen), color, LineThickness);

        // --- ТЕКСТ ---
        if (_isLocked && _currentSize < BaseSize * 1.5f)
        {
            var font = ThemeDB.FallbackFont;
            string info = $"TRG: {_targetName}, DST: {_targetDistance:F1}m";
            DrawString(font, center + new Vector2(halfSize + 5, -halfSize), info, HorizontalAlignment.Left, -1, 10, color);
        }

        // --- ДИНАМИЧЕСКАЯ ЦЕНТРАЛЬНАЯ ТОЧКА ---

        // 1. Расчет размера (Inverse Lerp)
        // Чем ближе (ближе к 0), тем больше (DotMaxSize). Чем дальше (к MaxEffectDistance), тем меньше.
        float distFactor = Mathf.Clamp(_targetDistance / MaxEffectDistance, 0f, 1f);
        float currentDotRadius = Mathf.Lerp(DotMaxSize, DotMinSize, distFactor);

        // 2. Расчет прозрачности (Fade)
        // Если дистанция меньше Start, начинаем уменьшать Alpha. Если меньше End — Alpha = 0.
        float alpha = 1.0f;
        if (_targetDistance < FadeDistanceStart)
        {
            // Remap диапазона [FadeDistanceEnd, FadeDistanceStart] в [0, 1]
            alpha = Mathf.InverseLerp(FadeDistanceEnd, FadeDistanceStart, _targetDistance);
        }

        if (alpha > 0.01f)
        {
            Color dotColor = color;
            dotColor.A = alpha; // Применяем прозрачность
            DrawCircle(center, currentDotRadius, dotColor);
        }
    }

    private void HandleScanData(bool found, string name, float dist)
    {
        _isLocked = found;
        _targetName = name;
        _targetDistance = dist;
        // Важно: если у тебя нет цели (found == false), 
        // тебе нужно передавать дистанцию рейкаста (до стены), 
        // иначе точка "залипнет" на последнем значении.
        // Если рейкаст ничего не ударил, ставь dist = 1000f;
    }
}