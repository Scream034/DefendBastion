using Godot;
using Game.Entity.AI.Behaviors;

namespace Game.Entity.AI;

/// <summary>
/// Состояние атаки. Полностью делегирует всю логику движения и атаки
/// текущему <see cref="ICombatBehavior"/>, назначенному у AIEntity.
/// </summary>
public sealed class AttackState(AIEntity context) : State(context)
{
    public override void Enter()
    {
        GD.Print($"{_context.Name} входит в состояние Attack, используя поведение: {_context.CombatBehavior.GetType().Name}");
    }

    public override void Update(float delta)
    {
        // Вся сложность вынесена. AttackState просто вызывает Process у текущего поведения.
        _context.CombatBehavior?.Process(_context, delta);
    }
}