#nullable enable

using System;
using Godot;

namespace Game.UI;

// Типы сообщений для раскраски и приоритизации
public enum LogChannel
{
    Kernel,     // Системные сообщения (Белый/Серый)
    Network,    // Подключения (Циан)
    Weapon,     // Оружие/Бой (Оранжевый/Красный)
    Sensor,     // Окружение/Компас (Зеленоватый)
    Warning     // Критические ошибки (Ярко-красный + Глитч)
}

public readonly struct LogEntry(LogChannel channel, string message)
{
    public readonly LogChannel Channel = channel;
    public readonly string Message = message;
    public readonly float Timestamp = Time.GetTicksMsec() / 1000f;
}

/// <summary>
/// Центральная нервная система робота. 
/// Любой компонент может отправить сюда сигнал, не зная о существовании UI.
/// </summary>
public static class RobotBus
{
    // Событие, на которое подпишется UI
    public static event Action<LogEntry>? OnLogMessage;

    /// <summary>
    /// Found Target?, Target Name, Distance
    /// </summary>
    public static event Action<bool, string, float>? OnTargetScannerUpdate;

    public static void Log(LogChannel channel, string message) => OnLogMessage?.Invoke(new LogEntry(channel, message));
    public static void Sys(string msg) => Log(LogChannel.Kernel, msg);
    public static void Net(string msg) => Log(LogChannel.Network, msg);
    public static void Warn(string msg) => Log(LogChannel.Warning, msg);
    public static void Combat(string msg) => Log(LogChannel.Weapon, msg);

    public static void PublishScanData(bool found, string name = "", float dist = 0f)
    => OnTargetScannerUpdate?.Invoke(found, name, dist);
}