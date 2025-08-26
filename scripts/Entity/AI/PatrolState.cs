using Godot;

namespace Game.Entity.AI;

/// <summary>
/// Состояние патрулирования. ИИ бесцельно бродит по заданным точкам или в радиусе.
/// </summary>
public sealed class PatrolState(AIEntity context) : State(context)
{
    private Vector3 _patrolTargetPosition;

    // Для патрулирования можно использовать массив точек или просто случайные точки в радиусе.
    // Для простоты выберем второй вариант.
    private readonly float _patrolRadius = 20f;

    public override void Enter()
    {
        GD.Print($"{_context.Name} entering Patrol state.");
        PickNewPatrolPoint();
    }

    public override void Update(double delta)
    {
        // Если была дана внешняя команда атаковать цель, немедленно переключаемся.
        if (_context.CurrentTarget != null)
        {
            _context.ChangeState(new AttackState(_context));
            return;
        }

        // Если ИИ достиг точки патрулирования, выбираем новую.
        if (_context.GlobalPosition.DistanceTo(_patrolTargetPosition) < 1.0f)
        {
            PickNewPatrolPoint();
        }

        _context.MoveTowards(_patrolTargetPosition, (float)delta);
    }

    private void PickNewPatrolPoint()
    {
        var randomDirection = new Vector3(
            (float)GD.RandRange(-1.0, 1.0),
            0,
            (float)GD.RandRange(-1.0, 1.0)
        ).Normalized();

        _patrolTargetPosition = _context.GlobalPosition + randomDirection * _patrolRadius;
        GD.Print($"{_context.Name} new patrol point: {_patrolTargetPosition}");
    }
}