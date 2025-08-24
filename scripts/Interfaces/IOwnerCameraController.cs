using Godot;

namespace Game.Interfaces;

/// <summary>
/// Позволяет реализовать собственную логику управления камерой игрока.
/// </summary>
public interface IOwnerCameraController
{
    public void HandleInput(in InputEvent @event);
}