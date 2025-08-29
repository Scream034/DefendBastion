using Godot;

namespace Game.Entity.AI.States
{
    /// <summary>
    /// Состояние атаки. Делегирует логику текущему ICombatBehavior.
    /// Переходит в PursuitState, если цель пропадает из вида.
    /// </summary>
    public sealed class AttackState(AIEntity context) : State(context)
    {
        public override void Enter()
        {
            _context.SetMovementSpeed(_context.NormalSpeed);
            // Сообщаем боевому поведению, что мы вступаем в бой.
            _context.CombatBehavior?.EnterCombat(_context);
        }

        public override void Exit()
        {
            // Сообщаем боевому поведению, что мы выходим из боя, чтобы оно могло очистить свое состояние.
            _context.CombatBehavior?.ExitCombat(_context);
        }

        public override void Update(float delta)
        {
            // VITAL CHECK: Убеждаемся, что цель все еще существует в игре.
            if (_context.CurrentTarget == null || !GodotObject.IsInstanceValid(_context.CurrentTarget))
            {
                _context.ReturnToDefaultState();
                return;
            }

            // ПРОВЕРКА ЛИНИИ ВИДИМОСТИ (от "глаз" ИИ)
            if (!_context.HasLineOfSightTo(_context.CurrentTarget))
            {
                GD.Print($"{_context.Name} lost line of sight to {_context.CurrentTarget.Name}. Pursuing...");
                _context.LastKnownTargetPosition = _context.CurrentTarget.GlobalPosition;
                _context.ChangeState(new PursuitState(_context));
                return;
            }

            // Если цель в прямой видимости, делегируем логику боевому поведению.
            _context.CombatBehavior?.Process(_context, delta);
        }
    }
}