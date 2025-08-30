using Game.Entity.AI.Behaviors;
using Godot;

namespace Game.Entity.AI.States
{
    /// <summary>
    /// Состояние атаки. Управляет общей тактикой боя: сближение, удержание дистанции и делегирование конкретных действий ICombatBehavior.
    /// </summary>
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
            var currentTarget = _context.TargetingSystem.CurrentTarget;

            if (!GodotObject.IsInstanceValid(currentTarget))
            {
                _context.OnTargetEliminated();
                return;
            }

            if (_context.NeedsToReevaluateTarget())
            {
                _context.TargetingSystem.ClearTarget();
                return;
            }
            
            // 1. Проверяем линию видимости. Если ее нет - немедленно переходим в преследование.
            // Используем свойство-обертку для чистоты кода.
            if (!_context.HasLineOfSightToCurrentTarget)
            {
                GD.Print($"{_context.Name} lost line of sight to {currentTarget.Name}. Pursuing...");
                _context.ChangeState(new PursuitState(_context));
                return;
            }
            
            float distanceToTarget = _context.GlobalPosition.DistanceTo(currentTarget.GlobalPosition);
            float attackRange = _combatBehavior?.AttackRange ?? 15f;

            // 2. Проверяем дистанцию. Если цель слишком далеко, сближаемся.
            if (distanceToTarget > attackRange)
            {
                _context.MovementController.MoveTo(currentTarget.GlobalPosition);
                return;
            }

            // 3. Если мы в зоне досягаемости и видим цель, передаем управление боевому поведению.
            _combatBehavior?.Process(_context, delta);
        }
    }
}