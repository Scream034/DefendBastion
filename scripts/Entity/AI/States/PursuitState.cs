// --- ИЗМЕНЕНИЯ ---
// 1. В методе Enter() теперь используется `context.PursuitTargetPosition` вместо `context.LastKnownTargetPosition`.
//    Это гарантирует, что AI будет преследовать именно ту цель, которую он "запомнил",
//    даже если `LastKnownTargetPosition` обновится из-за краткого обнаружения другой цели.
// -----------------

using Godot;

namespace Game.Entity.AI.States
{
    public sealed class PursuitState(AIEntity context) : State(context)
    {
        private const float PursuitTimeout = 10.0f;
        private float _timer;

        public override void Enter()
        {
            _context.SetMovementSpeed(_context.Profile.MovementProfile.FastSpeed);
            // Используем новую, специально предназначенную для этого позицию.
            _context.MovementController.MoveTo(_context.PursuitTargetPosition);
            _context.LookController.SetInterestPoint(_context.PursuitTargetPosition);
            _timer = PursuitTimeout;
        }

        public override void Exit()
        {
            _context.LookController.SetInterestPoint(null);
        }

        public override void Update(float delta)
        {
            _timer -= delta;

            if (!_context.IsTargetValid)
            {
                _context.OnCurrentTargetInvalidated();
                return;
            }

            // Важно: в состоянии преследования мы ищем ЛЮБУЮ враждебную цель.
            // Если мы увидели любую цель (даже не ту, которую преследовали), надо атаковать.
            if (_context.HasLineOfSightToCurrentTarget)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            if (_context.MovementController.NavigationAgent.IsNavigationFinished() || _timer <= 0)
            {
                GD.Print($"{_context.Name} pursuit failed. Target not found.");
                _context.ReturnToDefaultState();
            }
        }
    }
}