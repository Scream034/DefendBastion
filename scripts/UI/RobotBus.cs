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

public readonly struct LogEntry
{
    public readonly LogChannel Channel;
    public readonly string Message;
    public readonly float Timestamp;

    public LogEntry(LogChannel channel, string message)
    {
        Channel = channel;
        Message = message;
        Timestamp = Time.GetTicksMsec() / 1000f;
    }
}

/// <summary>
/// Центральная нервная система робота. 
/// Любой компонент может отправить сюда сигнал, не зная о существовании UI.
/// </summary>
public static class RobotBus
{
    // Событие, на которое подпишется UI
    public static event Action<LogEntry>? OnLogMessage;

    public static void Log(LogChannel channel, string message)
    {
        OnLogMessage?.Invoke(new LogEntry(channel, message));
    }
    
    // Хелперы для краткости
    public static void Sys(string msg) => Log(LogChannel.Kernel, msg);
    public static void Net(string msg) => Log(LogChannel.Network, msg);
    public static void Warn(string msg) => Log(LogChannel.Warning, msg);
    public static void Combat(string msg) => Log(LogChannel.Weapon, msg);
}