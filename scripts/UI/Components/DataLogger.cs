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
/// Внутренняя структура для отслеживания отображаемой строки с учётом повторений.
/// </summary>
internal sealed class DisplayedLogLine
{
    public LogChannel Channel { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RepeatCount { get; set; } = 1;
    public RichTextLabel? Label { get; set; }

    /// <summary>
    /// Уникальный ключ для сравнения (канал + сообщение).
    /// Адрес НЕ учитывается — только тип и текст.
    /// </summary>
    public string GetKey() => $"{Channel}::{Message}";
}

/// <summary>
/// Продвинутый Sci-Fi логгер с кинематографичной анимацией скроллинга.
/// Поддерживает группировку одинаковых сообщений: MESSAGE (×N)
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

    [ExportGroup("Message Grouping")]
    /// <summary>
    /// Группировать ли повторяющиеся сообщения.
    /// </summary>
    [Export] public bool GroupRepeatedMessages { get; set; } = true;

    /// <summary>
    /// Анимировать ли обновление счётчика повторений.
    /// </summary>
    [Export] public bool AnimateRepeatCounter { get; set; } = true;

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

    // ══════════════════════════════════════════════════════════════
    // НОВАЯ АРХИТЕКТУРА: Разделение UI-лейблов и логических записей
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Пул RichTextLabel для отображения (всегда MaxLines штук).
    /// </summary>
    private readonly List<RichTextLabel> _labelPool = [];

    /// <summary>
    /// Логические записи лога с отслеживанием повторений.
    /// Индекс 0 = самая старая (вверху при Upwards) или самая новая (при Downwards).
    /// </summary>
    private readonly List<DisplayedLogLine> _logLines = [];

    /// <summary>
    /// Кэш для быстрого поиска последней строки по ключу.
    /// </summary>
    private DisplayedLogLine? _lastLogLine = null;

    private readonly Queue<LogEntry> _messageQueue = new();

    private bool _isAnimating = false;
    private bool _isResizing = false;
    private Tween? _resizeTween;

    // Кэш адресов для каждого канала (чтобы адрес был стабильным в пределах одного сообщения)
    private readonly Dictionary<LogChannel, string> _cachedAddresses = [];

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
        _labelPool.Clear();
        _logLines.Clear();
        _lastLogLine = null;

        for (int i = 0; i < MaxLines; i++)
        {
            var label = CreateLabel();
            label.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(label);
            _labelPool.Add(label);

            float targetY = CalculateTargetY(i);
            label.Position = new Vector2(0, targetY);
            UpdateSingleLineVisuals(label, i, hasContent: false);
        }
    }

    private void AnimateVisibleLinesChange(int oldCount, int newCount)
    {
        _resizeTween?.Kill();
        _isResizing = true;

        _resizeTween = CreateTween();
        _resizeTween.SetParallel(true);
        _resizeTween.SetTrans(Tween.TransitionType.Quart);
        _resizeTween.SetEase(Tween.EaseType.Out);

        float targetHeight = newCount * LineHeight;
        _resizeTween.TweenProperty(this, "custom_minimum_size:y", targetHeight, ResizeDuration);

        for (int i = 0; i < MaxLines; i++)
        {
            var label = _labelPool[i];
            float targetY = CalculateTargetY(i);
            bool hasContent = i < _logLines.Count && !string.IsNullOrEmpty(_logLines[i].Message);
            float targetAlpha = CalculateAlpha(i, hasContent);

            _resizeTween.TweenProperty(label, "position:y", targetY, ResizeDuration);
            _resizeTween.TweenProperty(label, "modulate:a", targetAlpha, ResizeDuration);
        }

        _resizeTween.Chain().TweenCallback(Callable.From(() =>
        {
            _isResizing = false;
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
        if (_isAnimating || _isResizing || _messageQueue.Count == 0) return;

        var entry = _messageQueue.Dequeue();

        // ══════════════════════════════════════════════════════════
        // КЛЮЧЕВАЯ ЛОГИКА: Проверяем на повторение
        // ══════════════════════════════════════════════════════════

        if (GroupRepeatedMessages && TryIncrementRepeat(entry))
        {
            // Сообщение было сгруппировано — обновляем отображение без скролла
            ProcessQueue(); // Продолжаем обработку очереди
            return;
        }

        // Новое уникальное сообщение — делаем скролл
        AnimateScroll(entry);
    }

    /// <summary>
    /// Проверяет, совпадает ли новое сообщение с последним.
    /// Если да — увеличивает счётчик и обновляет UI.
    /// </summary>
    /// <returns>true если сообщение было сгруппировано, false если это новое сообщение.</returns>
    private bool TryIncrementRepeat(LogEntry entry)
    {
        if (_lastLogLine == null) return false;

        // Сравниваем ТОЛЬКО канал и сообщение (адрес игнорируется!)
        string newKey = $"{entry.Channel}::{entry.Message}";
        string lastKey = _lastLogLine.GetKey();

        if (newKey != lastKey) return false;

        // ══════════════════════════════════════════════════════════
        // СОВПАДЕНИЕ! Увеличиваем счётчик
        // ══════════════════════════════════════════════════════════

        _lastLogLine.RepeatCount++;

        // Обновляем отображение
        if (_lastLogLine.Label != null)
        {
            UpdateLabelContent(_lastLogLine.Label, _lastLogLine);

            if (AnimateRepeatCounter)
            {
                AnimateCounterPulse(_lastLogLine.Label);
            }
        }

        return true;
    }

    /// <summary>
    /// Анимация "пульса" при обновлении счётчика.
    /// </summary>
    private void AnimateCounterPulse(RichTextLabel label)
    {
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Elastic);
        tween.SetEase(Tween.EaseType.Out);

        // Быстрое увеличение и возврат
        var originalScale = label.Scale;
        tween.TweenProperty(label, "scale", new Vector2(1.05f, 1.05f), 0.08f);
        tween.TweenProperty(label, "scale", originalScale, 0.15f);
    }

    private void AnimateScroll(LogEntry entry)
    {
        _isAnimating = true;

        // ══════════════════════════════════════════════════════════
        // Создаём новую логическую запись
        // ══════════════════════════════════════════════════════════

        var newLine = new DisplayedLogLine
        {
            Channel = entry.Channel,
            Message = entry.Message,
            RepeatCount = 1
        };

        RichTextLabel recycledLabel;

        if (Direction == LogDirection.Upwards)
        {
            // Новые строки снизу — recycled берём сверху
            recycledLabel = _labelPool[0];
            _labelPool.RemoveAt(0);
            _labelPool.Add(recycledLabel);

            // Логические записи: удаляем старейшую (сверху), добавляем новую (снизу)
            if (_logLines.Count >= MaxLines)
            {
                _logLines.RemoveAt(0);
            }
            _logLines.Add(newLine);
        }
        else
        {
            // Новые строки сверху — recycled берём снизу
            recycledLabel = _labelPool[^1];
            _labelPool.RemoveAt(_labelPool.Count - 1);
            _labelPool.Insert(0, recycledLabel);

            // Логические записи: удаляем старейшую (снизу), добавляем новую (сверху)
            if (_logLines.Count >= MaxLines)
            {
                _logLines.RemoveAt(_logLines.Count - 1);
            }
            _logLines.Insert(0, newLine);
        }

        // Привязываем лейбл к логической записи
        newLine.Label = recycledLabel;
        _lastLogLine = newLine;

        // Обновляем привязки для всех записей
        UpdateLabelBindings();

        // Генерируем новый адрес для этого сообщения
        RegenerateAddressForChannel(entry.Channel);

        UpdateLabelContent(recycledLabel, newLine);

        float startOffset = (Direction == LogDirection.Upwards) ? LineHeight : -LineHeight;
        int newLabelIndex = (Direction == LogDirection.Upwards) ? MaxLines - 1 : 0;
        recycledLabel.Position = new Vector2(0, CalculateTargetY(newLabelIndex) + startOffset);

        recycledLabel.VisibleRatio = 0;
        recycledLabel.Modulate = new Color(1, 1, 1, 1);

        var tween = CreateTween().SetParallel(true).SetTrans(Tween.TransitionType.Circ).SetEase(Tween.EaseType.Out);

        for (int i = 0; i < MaxLines; i++)
        {
            var lbl = _labelPool[i];
            float targetY = CalculateTargetY(i);
            bool hasContent = i < _logLines.Count && !string.IsNullOrEmpty(_logLines[i].Message);
            float targetAlpha = CalculateAlpha(i, hasContent);

            tween.TweenProperty(lbl, "position:y", targetY, ScrollDuration);

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

    /// <summary>
    /// Обновляет привязки лейблов к логическим записям после скролла.
    /// </summary>
    private void UpdateLabelBindings()
    {
        for (int i = 0; i < _logLines.Count && i < _labelPool.Count; i++)
        {
            _logLines[i].Label = _labelPool[i];
        }
    }

    /// <summary>
    /// Генерирует новый случайный адрес для канала.
    /// </summary>
    private void RegenerateAddressForChannel(LogChannel channel)
    {
        _cachedAddresses[channel] = $":{GD.Randi() % 0xFFF:X3}";
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
            ClipContents = false,
            PivotOffset = new Vector2(0, LineHeight / 2f) // Для анимации scale от центра
        };

        if (LogFont != null) l.AddThemeFontOverride("normal_font", LogFont);
        l.AddThemeFontSizeOverride("normal_font_size", FontSize);

        return l;
    }

    /// <summary>
    /// Обновляет содержимое лейбла с учётом счётчика повторений.
    /// </summary>
    private void UpdateLabelContent(RichTextLabel label, DisplayedLogLine line)
    {
        string colorHex = ChannelColors.TryGetValue(line.Channel, out Color c) ? c.ToHtml() : "ffffff";
        string tick = ShowTicks ? $"[color=#55{colorHex}]{Engine.GetFramesDrawn():D7}[/color] " : "";
        string prefix = GeneratePrefix(line.Channel);

        // ══════════════════════════════════════════════════════════
        // СЧЁТЧИК ПОВТОРЕНИЙ
        // ══════════════════════════════════════════════════════════
        string repeatSuffix = "";
        if (line.RepeatCount > 1)
        {
            // Стиль: (×42) с другим оттенком
            repeatSuffix = $" [color=#ff{colorHex}](×{line.RepeatCount})[/color]";
        }

        string messageText = line.Message.ToUpper();
        string content = $"[color=#{colorHex}aa]{prefix}[/color] [color=#{colorHex}]{messageText}[/color]{repeatSuffix}";

        if (line.Channel == LogChannel.Warning)
        {
            content = $"[shake rate=10 level=5]{content}[/shake]";
        }

        label.Text = $"{tick}{content}";
    }

    // Для обратной совместимости (если где-то используется старый метод)
    private void SetupLabelContent(RichTextLabel label, LogEntry entry)
    {
        var tempLine = new DisplayedLogLine
        {
            Channel = entry.Channel,
            Message = entry.Message,
            RepeatCount = 1
        };
        UpdateLabelContent(label, tempLine);
    }

    private float CalculateTargetY(int index)
    {
        return index * LineHeight;
    }

    /// <summary>
    /// Вычисляет альфу с учётом VisibleLines.
    /// </summary>
    private float CalculateAlpha(int index, bool hasContent)
    {
        if (index >= _visibleLines)
            return 0f;

        if (!hasContent)
            return 0f;

        float weight;
        if (Direction == LogDirection.Upwards)
        {
            weight = (float)index / (_visibleLines - 1);
        }
        else
        {
            weight = (float)(_visibleLines - 1 - index) / (_visibleLines - 1);
        }

        return Mathf.Lerp(MinAlpha, MaxAlpha, weight);
    }

    private void UpdateSingleLineVisuals(RichTextLabel label, int index, bool hasContent)
    {
        float alpha = CalculateAlpha(index, hasContent);
        label.Modulate = new Color(1, 1, 1, alpha);
    }

    private string GeneratePrefix(LogChannel channel)
    {
        string addr = "";
        if (ShowMemoryAddresses)
        {
            // Используем кэшированный адрес если есть
            if (!_cachedAddresses.TryGetValue(channel, out addr!))
            {
                addr = $":{GD.Randi() % 0xFFF:X3}";
                _cachedAddresses[channel] = addr;
            }
        }

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
    /// </summary>
    public void SetVisibleLinesImmediate(int count)
    {
        _visibleLines = Mathf.Clamp(count, 1, MaxLines);
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, _visibleLines * LineHeight);

        for (int i = 0; i < _labelPool.Count; i++)
        {
            var label = _labelPool[i];
            label.Position = new Vector2(0, CalculateTargetY(i));
            bool hasContent = i < _logLines.Count && !string.IsNullOrEmpty(_logLines[i].Message);
            UpdateSingleLineVisuals(label, i, hasContent);
        }
    }

    /// <summary>
    /// Очистить лог полностью.
    /// </summary>
    public void Clear()
    {
        _logLines.Clear();
        _lastLogLine = null;
        _messageQueue.Clear();

        foreach (var label in _labelPool)
        {
            label.Text = "";
        }
    }
}