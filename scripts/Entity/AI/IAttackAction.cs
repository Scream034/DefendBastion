#nullable enable

using Godot;

namespace Game.Entity.AI;

/// <summary>
/// Интерфейс для конкретного действия атаки (выстрел, удар и т.д.).
/// Не содержит логики движения, только само действие.
/// </summary>
public interface IAttackAction
{
    /// <summary>
    /// Точка, из которой производится атака (например, дуло оружия).
    /// </summary>
    public Marker3D? MuzzlePoint { get; }

    /// <summary>
    /// Время перезарядки между выполнениями этого действия в секундах.
    /// </summary>
    public float AttackCooldown { get; }

    /// <summary>
    /// Выполнить действие атаки.
    /// </summary>
    void Execute(AIEntity context, LivingEntity target, Vector3 aimPosition);
}