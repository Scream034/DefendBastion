using Game.Interfaces;
using Godot;

namespace Game.Entity.AI.States;

/// <summary>
/// Состояние преследования. ИИ движется к последней известной позиции цели.
/// </summary>
public sealed class PursuitState(AIEntity context) : State(context)
{
    private const float PursuitTimeout = 10.0f; // Макс. время преследования в секундах
    private float _timer;

    public override void Enter()
    {
        GD.Print($"{_context.Name} entering Pursuit state, moving to {_context.LastKnownTargetPosition}");
        _context.SetMovementSpeed(_context.FastSpeed);
        _context.MoveTo(_context.LastKnownTargetPosition);
        _timer = PursuitTimeout;
    }

    public override void Update(float delta)
    {
        _timer -= delta;

        // VITAL CHECK: Убеждаемся, что цель все еще существует в игре.
        if (_context.CurrentTarget == null || !GodotObject.IsInstanceValid(_context.CurrentTarget))
        {
            GD.Print($"{_context.Name} target was destroyed during pursuit. Aborting.");
            _context.ReturnToDefaultState();
            return;
        }

        // Проверяем, жива ли цель
        if (_context.CurrentTarget is ICharacter character && character.Health <= 0)
        {
            var destroyedTarget = _context.CurrentTarget;
            GD.Print($"{_context.Name} target [{destroyedTarget.Name}] was destroyed during pursuit.");

            // Проверяем, был ли это контейнер
            if (destroyedTarget is IContainerEntity container)
            {
                var containedEntity = container.GetContainedEntity();
                if (GodotObject.IsInstanceValid(containedEntity) && _context.IsHostile(containedEntity))
                {
                    GD.Print($"{_context.Name}: Switching pursuit to contained entity [{containedEntity.Name}].");
                    // Устанавливаем пилота как новую цель и немедленно переходим в атаку,
                    // так как мы, скорее всего, уже близко.
                    _context.SetAttackTarget(containedEntity);
                    _context.ChangeState(new AttackState(_context));
                    return;
                }
            }

            // Если это был не контейнер или он пуст, прекращаем преследование.
            _context.ReturnToDefaultState();
            return;
        }

        // Если цель снова появилась в поле зрения
        if (_context.HasLineOfSightTo(_context.CurrentTarget))
        {
            GD.Print($"{_context.Name} reacquired target during pursuit!");
            _context.ChangeState(new AttackState(_context));
            return;
        }

        // Если добежали до места, а цели нет, или вышло время
        if (_context.NavigationAgent.IsNavigationFinished() || _timer <= 0)
        {
            GD.Print($"{_context.Name} pursuit failed. Target not found.");
            _context.ReturnToDefaultState();
        }
    }
}