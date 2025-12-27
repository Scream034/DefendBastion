#nullable enable

using Godot;
using System;

namespace Game.Singletons;

/// <summary>
/// Глобальный менеджер настроек.
/// Отвечает за хранение конфигурации, сохранение в файл и применение системных настроек.
/// Реализует паттерн Singleton (Autoload).
/// </summary>
public partial class GlobalSettings : Node
{
    // --- КОНФИГУРАЦИЯ ПО УМОЛЧАНИЮ (DEFAULTS & LIMITS) ---

    // Controls
    public const float DefaultMouseSensitivity = 1.0f;
    public const float MinMouseSensitivity = 0.05f;
    public const float MaxMouseSensitivity = 5.0f;
    public const bool DefaultInvertY = false;

    // Video
    public const float DefaultFieldOfView = 80.0f;
    public const float MinFieldOfView = 60.0f;
    public const float MaxFieldOfView = 120.0f;
    
    public static readonly Vector2I DefaultWindowResolution = new(1280, 720);
    public static readonly Vector2I MinWindowResolution = new(1024, 576);
    
    public const bool DefaultFullscreen = false;
    public const bool DefaultMaximized = true;

    // Audio
    public const float DefaultMasterVolume = 0.8f; // 1.0 = 100%
    public const float MinMasterVolume = 0.0f;
    public const float MaxMasterVolume = 1.0f;

    // --- СИСТЕМНЫЕ КОНСТАНТЫ ---

    public static GlobalSettings Instance { get; private set; } = null!;
    
    private const string SavePath = "user://settings.cfg";
    private const string SectionControls = "Controls";
    private const string SectionVideo = "Video";
    private const string SectionAudio = "Audio";

    // --- СИГНАЛЫ ---

    [Signal] public delegate void OnMouseSensitivityChangedEventHandler(float newValue);
    [Signal] public delegate void OnFovChangedEventHandler(float newValue);
    [Signal] public delegate void OnResolutionChangedEventHandler(Vector2I newResolution);
    [Signal] public delegate void OnSettingsLoadedEventHandler();

    // --- ПОЛЯ НАСТРОЕК ---

    // Controls
    private float _mouseSensitivity = DefaultMouseSensitivity;
    public float MouseSensitivity
    {
        get => _mouseSensitivity;
        set
        {
            _mouseSensitivity = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);
            EmitSignal(SignalName.OnMouseSensitivityChanged, _mouseSensitivity);
        }
    }

    private bool _invertY = DefaultInvertY;
    public bool InvertY
    {
        get => _invertY;
        set => _invertY = value;
    }

    // Video
    private float _fieldOfView = DefaultFieldOfView;
    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            _fieldOfView = Mathf.Clamp(value, MinFieldOfView, MaxFieldOfView);
            EmitSignal(SignalName.OnFovChanged, _fieldOfView);
        }
    }

    private Vector2I _windowResolution = DefaultWindowResolution;
    public Vector2I WindowResolution
    {
        get => _windowResolution;
        set
        {
            _windowResolution = new Vector2I(
                Mathf.Max(value.X, MinWindowResolution.X), 
                Mathf.Max(value.Y, MinWindowResolution.Y)
            );
            EmitSignal(SignalName.OnResolutionChanged, _windowResolution);
            ApplyVideoSettings();
        }
    }

    private bool _maximized = DefaultMaximized;
    public bool Maximized
    {
        get => _maximized;
        set
        {
            _maximized = value;
            ApplyVideoSettings();
        }
    }

    private bool _fullscreen = DefaultFullscreen;
    public bool Fullscreen
    {
        get => _fullscreen;
        set
        {
            _fullscreen = value;
            ApplyVideoSettings();
        }
    }

    // Audio
    private float _masterVolume = DefaultMasterVolume;
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Mathf.Clamp(value, MinMasterVolume, MaxMasterVolume);
            SetBusVolume("Master", _masterVolume);
        }
    }

    // --- ЖИЗНЕННЫЙ ЦИКЛ ---

    public override void _Ready()
    {
        Instance = this;
        // Используем CallDeferred, чтобы гарантировать, что дерево сцены готово
        // перед применением тяжелых настроек видео.
        CallDeferred(MethodName.LoadSettings);
    }

    /// <summary>
    /// Сохраняет текущие настройки в user://settings.cfg
    /// </summary>
    public void SaveSettings()
    {
        var config = new ConfigFile();

        // Controls
        config.SetValue(SectionControls, "MouseSensitivity", MouseSensitivity);
        config.SetValue(SectionControls, "InvertY", InvertY);

        // Video
        config.SetValue(SectionVideo, "FOV", FieldOfView);
        config.SetValue(SectionVideo, "Fullscreen", Fullscreen);
        config.SetValue(SectionVideo, "Maximized", Maximized);
        config.SetValue(SectionVideo, "Resolution", WindowResolution);

        // Audio
        config.SetValue(SectionAudio, "MasterVolume", MasterVolume);

        Error err = config.Save(SavePath);
        if (err != Error.Ok)
        {
            GD.PushError($"Failed to save settings: {err}");
        }
    }

    /// <summary>
    /// Загружает настройки. Использует Defaults, если значения не найдены.
    /// </summary>
    public void LoadSettings()
    {
        var config = new ConfigFile();
        Error err = config.Load(SavePath);

        if (err != Error.Ok)
        {
            GD.Print("Settings file not found. Applying defaults.");
            // Принудительно применяем дефолты (которые уже заданы при инициализации полей),
            // но нужно вызвать ApplyVideoSettings, чтобы окно настроилось.
            ApplyVideoSettings();
            MasterVolume = DefaultMasterVolume; // Применяем звук
            return;
        }

        // Читаем в приватные поля
        _mouseSensitivity = (float)config.GetValue(SectionControls, "MouseSensitivity", DefaultMouseSensitivity);
        _invertY = (bool)config.GetValue(SectionControls, "InvertY", DefaultInvertY);

        _fieldOfView = (float)config.GetValue(SectionVideo, "FOV", DefaultFieldOfView);
        _fullscreen = (bool)config.GetValue(SectionVideo, "Fullscreen", DefaultFullscreen);
        _maximized = (bool)config.GetValue(SectionVideo, "Maximized", DefaultMaximized);
        _windowResolution = (Vector2I)config.GetValue(SectionVideo, "Resolution", DefaultWindowResolution);

        // Для громкости вызываем свойство, чтобы сразу обновить AudioServer
        MasterVolume = (float)config.GetValue(SectionAudio, "MasterVolume", DefaultMasterVolume);

        // Применяем видео-настройки пачкой
        ApplyVideoSettings();

        EmitSignal(SignalName.OnSettingsLoaded);
        GD.Print("Settings loaded successfully.");
    }

    /// <summary>
    /// Сброс всех настроек к заводским значениям.
    /// Полезно для кнопки "Reset to Defaults" в UI.
    /// </summary>
    public void ResetToDefaults()
    {
        MouseSensitivity = DefaultMouseSensitivity;
        InvertY = DefaultInvertY;
        FieldOfView = DefaultFieldOfView;
        WindowResolution = DefaultWindowResolution;
        Maximized = DefaultMaximized;
        Fullscreen = DefaultFullscreen;
        MasterVolume = DefaultMasterVolume;
        
        SaveSettings();
        GD.Print("Settings reset to defaults.");
    }

    private void ApplyVideoSettings()
    {
        // 1. Приоритет Fullscreen
        if (_fullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
        }
        // 2. Приоритет Maximized
        else if (_maximized)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
        }
        // 3. Оконный режим
        else
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(_windowResolution);
            CenterWindow();
        }
    }

    private void CenterWindow()
    {
        int screenIndex = DisplayServer.WindowGetCurrentScreen();
        Rect2I screenRect = DisplayServer.ScreenGetUsableRect(screenIndex);
        var position = screenRect.Position + (screenRect.Size - _windowResolution) / 2;
        DisplayServer.WindowSetPosition(position);
    }

    private void SetBusVolume(string busName, float linearVolume)
    {
        int busIndex = AudioServer.GetBusIndex(busName);
        if (busIndex == -1) return;

        float db = Mathf.LinearToDb(linearVolume);
        AudioServer.SetBusVolumeDb(busIndex, db);
        AudioServer.SetBusMute(busIndex, linearVolume <= 0.001f);
    }
}
#nullable disable