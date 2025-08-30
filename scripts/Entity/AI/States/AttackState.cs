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
                _context.OnCurrentTargetInvalidated();
                return;
            }

            var currentTarget = _context.TargetingSystem.CurrentTarget;

            if (!_context.HasLineOfSightToCurrentTarget)
            {
                HandleLostLineOfSight(currentTarget);
                return;
            }

            float distanceToTarget = _context.GlobalPosition.DistanceTo(currentTarget.GlobalPosition);
            float attackRange = _combatBehavior?.AttackRange ?? 15f;

            if (distanceToTarget > attackRange)
            {
                _context.MovementController.MoveTo(currentTarget.GlobalPosition);
                return;
            }

            _combatBehavior?.Process(_context, delta);
        }

        /// <summary>
        /// Обрабатывает ситуацию, когда линия видимости до текущей цели потеряна.
        /// Реализует тактическое решение: сначала найти другую цель, и только потом преследовать.
        /// </summary>
        private void HandleLostLineOfSight(LivingEntity lostTarget)
        {
            GD.Print($"{_context.Name} lost line of sight to {lostTarget.Name}. Re-evaluating targets...");

            // Сохраняем позицию цели, которую мы потеряли, на случай если придется ее преследовать.
            _context.SetPursuitTargetPosition(lostTarget.GlobalPosition);

            // Форсируем немедленную переоценку, чтобы найти другую видимую цель.
            _context.TargetingSystem.ForceReevaluation();

            // Проверяем результат переоценки.
            if (_context.IsTargetValid)
            {
                // Нашлась новая цель! Остаемся в AttackState и на следующем кадре начнем атаковать ее.
                GD.Print($"{_context.Name} found new immediate target: {_context.TargetingSystem.CurrentTarget.Name}. Engaging.");
            }
            else
            {
                // Других видимых целей нет. Теперь самое время начать преследование.
                GD.Print($"{_context.Name} no other visible targets. Pursuing last known position of {lostTarget.Name}.");
                _context.ChangeState(new PursuitState(_context));
            }
        }
    }
}