#nullable enable

using Godot;
using System.Collections.Generic;

namespace Game.UI.Components;

/// <summary>
/// Направление скроллинга лога.
/// </summary>
public enum LogDirection
{
    /// <summary> Строки ползут вверх (как в терминале). Новые снизу. </summary>
    Upwards,
    /// <summary> Строки ползут вниз. Новые сверху. </summary>
    Downwards
}

/// <summary>
/// Продвинутый Sci-Fi логгер с кинематографичной анимацией скроллинга.
/// Поддерживает динамическое изменение количества видимых строк с анимацией.
/// </summary>
[GlobalClass]
public partial class DataLogger : Control
{
    [ExportGroup("Layout Settings")]
    [Export] public int MaxLines { get; set; } = 12;
    [Export] public int LineHeight { get; set; } = 20;
    [Export] public LogDirection Direction { get; set; } = LogDirection.Downwards;

    [ExportGroup("Animation")]
    [Export(PropertyHint.Range, "0.05, 1.0")] public float ScrollDuration { get; set; } = 0.15f;
    [Export(PropertyHint.Range, "0.1, 0.5")] public float ResizeDuration { get; set; } = 0.25f;

    [ExportGroup("Visual Aesthetics")]
    [Export] public Font? LogFont { get; set; }
    [Export] public int FontSize { get; set; } = 14;
    [Export] public bool ShowTicks { get; set; } = true;
    [Export] public bool ShowMemoryAddresses { get; set; } = true;

    [ExportGroup("Fading")]
    [Export(PropertyHint.Range, "0,1")] public float MinAlpha { get; set; } = 0.1f;
    [Export(PropertyHint.Range, "0,1")] public float MaxAlpha { get; set; } = 1.0f;

    [Export]
    public Godot.Collections.Dictionary<LogChannel, Color> ChannelColors { get; set; } = new()
    {
        { LogChannel.Kernel,  new Color("#888888") },
        { LogChannel.Network, new Color("#00aaff") },
        { LogChannel.Weapon,  new Color("#ffaa00") },
        { LogChannel.Sensor,  new Color("#00ff88") },
        { LogChannel.Warning, new Color("#ff4444") }
    };

    // Количество видимых строк с поддержкой анимации
    private int _visibleLines = 12;
    public int VisibleLines
    {
        get => _visibleLines;
        set
        {
            int clamped = Mathf.Clamp(value, 1, MaxLines);
            if (clamped != _visibleLines)
            {
                int oldValue = _visibleLines;
                _visibleLines = clamped;
                AnimateVisibleLinesChange(oldValue, _visibleLines);
            }
        }
    }

    // Внутренний пул строк
    private readonly List<RichTextLabel> _lines = [];
    private readonly Queue<LogEntry> _messageQueue = new();

    private bool _isAnimating = false;
    private bool _isResizing = false;
    private Tween? _resizeTween;

    public override void _Ready()
    {
        ClipContents = true;
        _visibleLines = Mathf.Clamp(_visibleLines, 1, MaxLines);
        CustomMinimumSize = new Vector2(200, _visibleLines * LineHeight);

        InitializePool();
        RobotBus.OnLogMessage += EnqueueMessage;
    }

    public override void _ExitTree()
    {
        RobotBus.OnLogMessage -= EnqueueMessage;
    }

    /// <summary>
    /// Создает полный пул лейблов (MaxLines) и расставляет их.
    /// </summary>
    private void InitializePool()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _lines.Clear();

        for (int i = 0; i < MaxLines; i++)
        {
            var label = CreateLabel();
            label.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(label);
            _lines.Add(label);

            float targetY = CalculateTargetY(i);
            label.Position = new Vector2(0, targetY);

            // Строки за пределами VisibleLines - невидимы
            UpdateSingleLineVisuals(label, i);
        }
    }

    /// <summary>
    /// Анимирует изменение количества видимых строк.
    /// </summary>
    private void AnimateVisibleLinesChange(int oldCount, int newCount)
    {
        // Отменяем предыдущую анимацию ресайза если есть
        _resizeTween?.Kill();
        _isResizing = true;

        _resizeTween = CreateTween();
        _resizeTween.SetParallel(true);
        _resizeTween.SetTrans(Tween.TransitionType.Quart);
        _resizeTween.SetEase(Tween.EaseType.Out);

        // Анимируем размер контейнера
        float targetHeight = newCount * LineHeight;
        _resizeTween.TweenProperty(this, "custom_minimum_size:y", targetHeight, ResizeDuration);

        // Анимируем каждую строку
        for (int i = 0; i < MaxLines; i++)
        {
            var label = _lines[i];
            float targetY = CalculateTargetY(i);
            float targetAlpha = CalculateAlpha(i);

            // Позиция
            _resizeTween.TweenProperty(label, "position:y", targetY, ResizeDuration);

            // Прозрачность - строки за пределами плавно исчезают
            _resizeTween.TweenProperty(label, "modulate:a", targetAlpha, ResizeDuration);
        }

        _resizeTween.Chain().TweenCallback(Callable.From(() =>
        {
            _isResizing = false;
            // Обрабатываем очередь после завершения ресайза
            ProcessQueue();
        }));
    }

    private void EnqueueMessage(LogEntry entry)
    {
        _messageQueue.Enqueue(entry);
        ProcessQueue();
    }

    private void ProcessQueue()
    {
        // Не обрабатываем очередь во время ресайза или анимации
        if (_isAnimating || _isResizing || _messageQueue.Count == 0) return;

        var entry = _messageQueue.Dequeue();
        AnimateScroll(entry);
    }

    private void AnimateScroll(LogEntry entry)
    {
        _isAnimating = true;

        RichTextLabel recycledLabel;

        if (Direction == LogDirection.Upwards)
        {
            recycledLabel = _lines[0];
            _lines.RemoveAt(0);
            _lines.Add(recycledLabel);
        }
        else
        {
            recycledLabel = _lines[^1];
            _lines.RemoveAt(_lines.Count - 1);
            _lines.Insert(0, recycledLabel);
        }

        SetupLabelContent(recycledLabel, entry);

        float startOffset = (Direction == LogDirection.Upwards) ? LineHeight : -LineHeight;
        int newLabelIndex = (Direction == LogDirection.Upwards) ? MaxLines - 1 : 0;
        recycledLabel.Position = new Vector2(0, CalculateTargetY(newLabelIndex) + startOffset);

        recycledLabel.VisibleRatio = 0;
        recycledLabel.Modulate = new Color(1, 1, 1, 1);

        var tween = CreateTween().SetParallel(true).SetTrans(Tween.TransitionType.Circ).SetEase(Tween.EaseType.Out);

        for (int i = 0; i < MaxLines; i++)
        {
            var lbl = _lines[i];
            float targetY = CalculateTargetY(i);
            float targetAlpha = CalculateAlpha(i);

            tween.TweenProperty(lbl, "position:y", targetY, ScrollDuration);

            // Новая строка всегда яркая (если в пределах видимости)
            if (lbl == recycledLabel && i < _visibleLines)
                targetAlpha = MaxAlpha;

            tween.TweenProperty(lbl, "modulate:a", targetAlpha, ScrollDuration);
        }

        tween.TweenProperty(recycledLabel, "visible_ratio", 1.0f, ScrollDuration + 0.1f);

        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _isAnimating = false;
            ProcessQueue();
        }));
    }

    private RichTextLabel CreateLabel()
    {
        var l = new RichTextLabel
        {
            FitContent = false,
            ScrollActive = false,
            BbcodeEnabled = true,
            MouseFilter = MouseFilterEnum.Ignore,
            Size = new Vector2(Size.X, LineHeight),
            ClipContents = false
        };

        if (LogFont != null) l.AddThemeFontOverride("normal_font", LogFont);
        l.AddThemeFontSizeOverride("normal_font_size", FontSize);

        return l;
    }

    private void SetupLabelContent(RichTextLabel label, LogEntry entry)
    {
        string colorHex = ChannelColors.TryGetValue(entry.Channel, out Color c) ? c.ToHtml() : "ffffff";
        string tick = ShowTicks ? $"[color=#55{colorHex}]{Engine.GetFramesDrawn():D7}[/color] " : "";
        string prefix = GeneratePrefix(entry.Channel);

        string content = $"[color={colorHex}aa]{prefix}[/color] [color={colorHex}]{entry.Message.ToUpper()}[/color]";

        if (entry.Channel == LogChannel.Warning)
        {
            content = $"[shake rate=10 level=5]{content}[/shake]";
        }

        label.Text = $"{tick}{content}";
    }

    private float CalculateTargetY(int index)
    {
        return index * LineHeight;
    }

    /// <summary>
    /// Вычисляет альфу с учётом VisibleLines.
    /// Строки за пределами видимости получают альфу 0.
    /// </summary>
    private float CalculateAlpha(int index)
    {
        // Строки за пределами видимости - полностью прозрачные
        if (index >= _visibleLines)
            return 0f;

        // Пустые строки тоже прозрачные
        if (index < _lines.Count && string.IsNullOrEmpty(_lines[index].Text))
            return 0f;

        float weight;
        if (Direction == LogDirection.Upwards)
        {
            // Index 0 (верх) -> Старое -> MinAlpha
            // Index VisibleLines-1 (низ видимой области) -> Новое -> MaxAlpha
            weight = (float)index / (_visibleLines - 1);
        }
        else
        {
            // Index 0 (верх) -> Новое -> MaxAlpha
            // Index VisibleLines-1 (низ видимой области) -> Старое -> MinAlpha
            weight = (float)(_visibleLines - 1 - index) / (_visibleLines - 1);
        }

        return Mathf.Lerp(MinAlpha, MaxAlpha, weight);
    }

    private void UpdateSingleLineVisuals(RichTextLabel label, int index)
    {
        float alpha = CalculateAlpha(index);
        label.Modulate = new Color(1, 1, 1, alpha);
    }

    private string GeneratePrefix(LogChannel channel)
    {
        string addr = ShowMemoryAddresses ? $":{GD.Randi() % 0xFFF:X3}" : "";
        return channel switch
        {
            LogChannel.Kernel => $"[SYS{addr}]",
            LogChannel.Network => $"[NET{addr}]",
            LogChannel.Weapon => $"[WPN{addr}]",
            LogChannel.Sensor => $"[SNS{addr}]",
            LogChannel.Warning => $"[!ERR{addr}]",
            _ => $"[DAT{addr}]"
        };
    }

    /// <summary>
    /// Принудительно установить количество видимых строк без анимации.
    /// Используется при инициализации.
    /// </summary>
    public void SetVisibleLinesImmediate(int count)
    {
        _visibleLines = Mathf.Clamp(count, 1, MaxLines);
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, _visibleLines * LineHeight);

        for (int i = 0; i < _lines.Count; i++)
        {
            var label = _lines[i];
            label.Position = new Vector2(0, CalculateTargetY(i));
            UpdateSingleLineVisuals(label, i);
        }
    }
}