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
            GD.Print($"{_context.Name} entering Investigate state, moving to {_context.InvestigationPosition}");
            _context.SetMovementSpeed(_context.NormalSpeed);
            _context.MoveTo(_context.InvestigationPosition);
            _timer = InvestigationTime;

            // Говорим ИИ следить за точкой расследования во время движения.
            _context.SetLookTarget(_context.InvestigationPosition);
        }

        public override void Exit()
        {
            // При выходе из состояния сбрасываем точку интереса.
            _context.SetLookTarget(null);
        }

        public override void Update(float delta)
        {
            if (_context.CurrentTarget != null)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            if (!_isAtLocation)
            {
                if (_context.NavigationAgent.IsNavigationFinished())
                {
                    _isAtLocation = true;
                    GD.Print($"{_context.Name} reached investigation point. Looking around...");
                }
            }
            else
            {
                // Когда дошли до места, продолжаем смотреть на него, осматриваясь.
                _context.RotateBodyTowards(_context.InvestigationPosition, delta * 0.5f);
                _context.RotateHeadTowards(_context.InvestigationPosition, delta);

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