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
            _context.MovementController.MoveTo(_context.LastKnownTargetPosition);
            _context.LookController.SetInterestPoint(_context.LastKnownTargetPosition);
            _timer = PursuitTimeout;
        }

        public override void Exit()
        {
            _context.LookController.SetInterestPoint(null);
        }

        public override void Update(float delta)
        {
            _timer -= delta;

            if (!GodotObject.IsInstanceValid(_context.TargetingSystem.CurrentTarget))
            {
                _context.OnTargetEliminated();
                return;
            }
            
            // Если мы снова увидели цель - возвращаемся в атаку.
            if (_context.HasLineOfSightToCurrentTarget)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            // Если дошли до точки или время вышло - преследование провалено.
            if (_context.MovementController.NavigationAgent.IsNavigationFinished() || _timer <= 0)
            {
                GD.Print($"{_context.Name} pursuit failed. Target not found.");
                _context.ReturnToDefaultState();
            }
        }
    }
}