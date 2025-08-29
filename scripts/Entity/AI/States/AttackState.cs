using Godot;

namespace Game.Entity.AI.States;

/// <summary>
/// Состояние атаки. Делегирует логику текущему ICombatBehavior.
/// Переходит в PursuitState, если цель пропадает из вида.
/// </summary>
public sealed class AttackState(AIEntity context) : State(context)
{
    public override void Enter()
    {
        _context.SetMovementSpeed(_context.NormalSpeed);
    }

    public override void Update(float delta)
    {
        // VITAL CHECK: Убеждаемся, что цель все еще существует в игре.
        // Это предотвращает сбои, если цель была уничтожена в этом же кадре.
        if (_context.CurrentTarget == null || !GodotObject.IsInstanceValid(_context.CurrentTarget))
        {
            _context.ReturnToDefaultState();
            return;
        }

        // ПРОВЕРКА ЛИНИИ ВИДИМОСТИ
        if (!_context.HasLineOfSightTo(_context.CurrentTarget))
        {
            GD.Print($"{_context.Name} lost line of sight to {_context.CurrentTarget.Name}. Pursuing...");
            // Запоминаем, где последний раз видели цель
            _context.LastKnownTargetPosition = _context.CurrentTarget.GlobalPosition;
            // Переходим в состояние преследования
            _context.ChangeState(new PursuitState(_context));
            return;
        }

        // Если цель в прямой видимости, продолжаем стандартное боевое поведение
        _context.CombatBehavior?.Process(_context, delta);
    }
}