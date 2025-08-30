using Godot;

namespace Game.Entity.AI.States
{
    public sealed class InvestigateState(AIEntity context) : State(context)
    {
        private const float InvestigationTime = 5.0f;
        private float _timer;
        private bool _isAtLocation = false;

        public override void Enter()
        {
            _context.SetMovementSpeed(_context.Profile.MovementProfile.NormalSpeed);
            _context.MovementController.MoveTo(_context.InvestigationPosition);
            _context.LookController.SetInterestPoint(_context.InvestigationPosition);
            _timer = InvestigationTime;
        }

        public override void Exit()
        {
            _context.LookController.SetInterestPoint(null);
        }

        public override void Update(float delta)
        {
            if (_context.TargetingSystem.CurrentTarget != null)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            if (!_isAtLocation && _context.MovementController.NavigationAgent.IsNavigationFinished())
            {
                _isAtLocation = true;
                GD.Print($"{_context.Name} reached investigation point. Looking around...");
            }

            if (_isAtLocation)
            {
                _timer -= delta;
                if (_timer <= 0)
                {
                    GD.Print($"{_context.Name} investigation complete. Nothing found.");
                    _context.ReturnToDefaultState();
                }
            }
        }
    }
}