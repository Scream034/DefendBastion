#nullable enable

using Godot;

namespace Game.Entity.AI;

/// <summary>
/// Интерфейс для конкретного действия атаки (выстрел, удар и т.д.).
/// Не содержит логики движения или перезарядки.
/// </summary>
public interface IAttackAction
{
    /// <summary>
    /// Точка, из которой производится атака (например, дуло оружия).
    /// Может быть null, если атака не имеет конкретной точки источника (например, AoE-атака вокруг себя).
    /// </summary>
    public Marker3D? MuzzlePoint { get; }

    /// <summary>
    /// Выполнить действие атаки.
    /// </summary>
    void Execute(AIEntity attacker, PhysicsBody3D target);
}