using Game.Entity.AI.Behaviors;
using Godot;

namespace Game.Entity.AI.States
{
    public sealed class AttackState(AIEntity context) : State(context)
    {
        private ICombatBehavior _combatBehavior;

        public override void Enter()
        {
            _context.SetMovementSpeed(_context.Profile.MovementProfile.NormalSpeed);
            _combatBehavior = _context.CombatBehavior;
            _combatBehavior?.EnterCombat(_context);
        }

        public override void Exit()
        {
            _combatBehavior?.ExitCombat(_context);
        }

        public override void Update(float delta)
        {
            if (!_context.IsTargetValid)
            {
                // Если цель стала невалидной (умерла, исчезла), выходим из боя.
                _context.OnCurrentTargetInvalidated();
                return;
            }

            var currentTarget = _context.TargetingSystem.CurrentTarget;
            float distanceToTarget = _context.GlobalPosition.DistanceTo(currentTarget.GlobalPosition);
            float attackRange = _combatBehavior?.AttackRange ?? 15f;

            // Если мы за пределами максимальной дальности атаки, подходим ближе.
            if (distanceToTarget > attackRange)
            {
                _context.MovementController.MoveTo(currentTarget.GlobalPosition);
                return; // Двигаемся, остальная логика на следующий кадр.
            }

            // Если мы в радиусе атаки, передаем управление боевому поведению.
            bool canContinueCombat = _combatBehavior?.Process(_context, delta) ?? false;

            // Если поведение сигнализирует, что оно больше не может вести бой
            // (например, потеряло цель за углом и не смогло найти новую позицию),
            // тогда переходим в преследование.
            if (!canContinueCombat)
            {
                GD.Print($"{_context.Name} combat behavior failed to engage {currentTarget.Name}. Pursuing last known position.");
                _context.SetPursuitTargetPosition(currentTarget.GlobalPosition);
                _context.ChangeState(new PursuitState(_context));
            }
        }
    }
}