#nullable enable

using Godot;
using System.Collections.Generic;

namespace Game.UI;

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
/// Использует систему очередей и Tween-анимации для плавного смещения строк.
/// </summary>
[GlobalClass]
public partial class DataLogger : Control
{
    [ExportGroup("Layout Settings")]
    [Export] public int MaxLines { get; set; } = 12;
    [Export] public int LineHeight { get; set; } = 20; // Высота одной строки в пикселях
    [Export] public LogDirection Direction { get; set; } = LogDirection.Downwards;

    [ExportGroup("Animation")]
    [Export(PropertyHint.Range, "0.05, 1.0")] public float ScrollDuration { get; set; } = 0.15f; // Скорость скролла

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

    // Внутренний пул строк
    private readonly List<RichTextLabel> _lines = [];
    // Очередь сообщений для обработки "бурстов" (когда приходит много логов сразу)
    private readonly Queue<LogEntry> _messageQueue = new();

    private bool _isAnimating = false;

    public override void _Ready()
    {
        // Важно: обрезаем содержимое, выходящее за границы контрола
        ClipContents = true;
        // Задаем минимальный размер контрола, чтобы он занимал место в верстке
        CustomMinimumSize = new Vector2(200, MaxLines * LineHeight);

        InitializePool();
        RobotBus.OnLogMessage += EnqueueMessage;
    }

    public override void _ExitTree()
    {
        RobotBus.OnLogMessage -= EnqueueMessage;
    }

    /// <summary>
    /// Создает пул лейблов и расставляет их по начальным позициям.
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

            // Начальная расстановка
            float targetY = CalculateTargetY(i);
            label.Position = new Vector2(0, targetY);

            // Сразу применяем прозрачность (пустые строки невидимы)
            UpdateSingleLineVisuals(label, i);
        }
    }

    /// <summary>
    /// Добавляет сообщение в очередь на обработку.
    /// </summary>
    private void EnqueueMessage(LogEntry entry)
    {
        _messageQueue.Enqueue(entry);
        ProcessQueue();
    }

    /// <summary>
    /// Запускает анимацию следующего сообщения, если система свободна.
    /// </summary>
    private void ProcessQueue()
    {
        if (_isAnimating || _messageQueue.Count == 0) return;

        var entry = _messageQueue.Dequeue();
        AnimateScroll(entry);
    }

    /// <summary>
    /// Основная магия. Сдвигает все строки и добавляет новую с анимацией.
    /// </summary>
    private void AnimateScroll(LogEntry entry)
    {
        _isAnimating = true;

        // 1. Логика ротации списка (Recycling)
        // Берем строку, которая уйдет за границы, и переиспользуем её как "новую"
        RichTextLabel recycledLabel;

        if (Direction == LogDirection.Upwards)
        {
            // Убираем самую верхнюю (индекс 0), она станет новой нижней
            recycledLabel = _lines[0];
            _lines.RemoveAt(0);
            _lines.Add(recycledLabel);
        }
        else // Downwards
        {
            // Убираем самую нижнюю, она станет новой верхней
            recycledLabel = _lines[^1];
            _lines.RemoveAt(_lines.Count - 1);
            _lines.Insert(0, recycledLabel);
        }

        // 2. Подготовка "новой" строки (она пока невидима или за границей)
        SetupLabelContent(recycledLabel, entry);

        // Ставим её в стартовую позицию (за пределами видимости перед въездом)
        // Если Upwards: она должна появиться снизу (index = MaxLines-1)
        // Если Downwards: она должна появиться сверху (index = 0)
        // Но так как мы уже повернули список _lines, мы можем просто использовать её текущий индекс в списке,
        // но добавить смещение offset для анимации въезда.

        float startOffset = (Direction == LogDirection.Upwards) ? LineHeight : -LineHeight;
        // Текущая "виртуальная" позиция до анимации
        int newLabelIndex = (Direction == LogDirection.Upwards) ? MaxLines - 1 : 0;
        recycledLabel.Position = new Vector2(0, CalculateTargetY(newLabelIndex) + startOffset);

        // Сброс анимации текста (Typewriter effect)
        recycledLabel.VisibleRatio = 0;
        recycledLabel.Modulate = new Color(1, 1, 1, 1); // Полная альфа (мы её потом понизим в цикле update, но новая должна быть яркой)

        // 3. Создаем Tween для массового сдвига
        var tween = CreateTween().SetParallel(true).SetTrans(Tween.TransitionType.Circ).SetEase(Tween.EaseType.Out);

        for (int i = 0; i < MaxLines; i++)
        {
            var lbl = _lines[i];
            float targetY = CalculateTargetY(i);

            // Анимируем позицию
            tween.TweenProperty(lbl, "position:y", targetY, ScrollDuration);

            // Анимируем прозрачность (Visual Aging)
            float targetAlpha = CalculateAlpha(i);
            // Если это новая строка, пусть она будет яркой, а не прозрачной сразу
            if (lbl == recycledLabel) targetAlpha = 1.0f;

            tween.TweenProperty(lbl, "modulate:a", targetAlpha, ScrollDuration);
        }

        // Дополнительный эффект для новой строки: появление текста (Typewriter)
        tween.TweenProperty(recycledLabel, "visible_ratio", 1.0f, ScrollDuration + 0.1f);

        // 4. Завершение
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _isAnimating = false;
            // Рекурсивно вызываем для следующего сообщения в очереди
            ProcessQueue();
        }));
    }

    private RichTextLabel CreateLabel()
    {
        var l = new RichTextLabel
        {
            FitContent = false, // Отключаем авто-размер, мы контролируем высоту сами
            ScrollActive = false,
            BbcodeEnabled = true,
            MouseFilter = MouseFilterEnum.Ignore,
            Size = new Vector2(Size.X, LineHeight), // Фиксируем размер
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
            // Shake effect не очень дружит с layout tweening, но внутри RichTextLabel работает
            content = $"[shake rate=10 level=5]{content}[/shake]";
        }

        label.Text = $"{tick}{content}";
    }

    /// <summary>
    /// Вычисляет Y координату для строки с указанным индексом.
    /// </summary>
    private float CalculateTargetY(int index)
    {
        // 0 - это всегда верх контрола
        return index * LineHeight;
    }

    private float CalculateAlpha(int index)
    {
        // 0 - верх (старое для Upwards, новое для Downwards)
        float weight;

        if (Direction == LogDirection.Upwards)
        {
            // Index 0 (верх) -> Старое -> MinAlpha
            // Index Max (низ) -> Новое -> MaxAlpha
            weight = (float)index / (MaxLines - 1);
        }
        else
        {
            // Index 0 (верх) -> Новое -> MaxAlpha
            // Index Max (низ) -> Старое -> MinAlpha
            weight = (float)(MaxLines - 1 - index) / (MaxLines - 1);
        }

        return Mathf.Lerp(MinAlpha, MaxAlpha, weight);
    }

    private void UpdateSingleLineVisuals(RichTextLabel label, int index)
    {
        if (string.IsNullOrEmpty(label.Text))
        {
            label.Modulate = Colors.Transparent;
            return;
        }
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
}