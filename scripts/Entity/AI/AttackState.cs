using Godot;

namespace Game.Entity.AI;

/// <summary>
/// Состояние атаки. ИИ преследует и атакует свою цель.
/// </summary>
public sealed class AttackState(AIEntity context) : State(context)
{
    // Простая симуляция перезарядки атаки
    private readonly double _attackCooldown = 2.0;
    private double _timeSinceLastAttack = 0;

    public override void Enter()
    {
        GD.Print($"{_context.Name} entering Attack state, target: {_context.CurrentTarget?.Name}");
    }

    public override void Update(double delta)
    {
        // Если цель исчезла (убита, вышла из игры), возвращаемся в патруль.
        if (_context.CurrentTarget == null || GodotObject.IsInstanceValid(_context.CurrentTarget) == false)
        {
            _context.ClearTarget();
            _context.ChangeState(new PatrolState(_context));
            return;
        }

        _timeSinceLastAttack += delta;
        float distanceToTarget = _context.GlobalPosition.DistanceTo(_context.CurrentTarget.GlobalPosition);

        // Если цель слишком далеко, преследуем ее.
        if (distanceToTarget > _context.AttackRange)
        {
            _context.MoveTowards(_context.CurrentTarget.GlobalPosition, (float)delta);
        }
        else // Если цель в зоне досягаемости атаки
        {
            // Останавливаемся и смотрим на цель
            _context.StopMovement((float)delta); // Передаем delta
            _context.LookAt(_context.CurrentTarget.GlobalPosition, Vector3.Up);

            // Атакуем, если перезарядка прошла
            if (_timeSinceLastAttack >= _attackCooldown)
            {
                PerformAttack();
                _timeSinceLastAttack = 0;
            }
        }
    }

    private void PerformAttack()
    {
        GD.Print($"{_context.Name} attacks {_context.CurrentTarget.Name}!");
        // Здесь будет логика нанесения урона
        // Например: _context.CurrentTarget.DamageAsync(25);
    }
}