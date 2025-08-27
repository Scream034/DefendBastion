using Godot;

namespace Game.Entity.AI.States
{
    /// <summary>
    /// Состояние расследования. ИИ движется к источнику угрозы (например, выстрела)
    /// и осматривается в поисках врага.
    /// </summary>
    public sealed class InvestigateState(AIEntity context) : State(context)
    {
        private const float InvestigationTime = 5.0f; // Время на "осмотр"
        private float _timer;
        private bool _isAtLocation = false;

        public override void Enter()
        {
            GD.Print($"[{_context.Name}] entering Investigate state, moving to {_context.InvestigationPosition}");
            _context.SetMovementSpeed(_context.NormalSpeed);
            _context.MoveTo(_context.InvestigationPosition);
            _timer = InvestigationTime;
        }

        public override void Update(float delta)
        {
            // Если в процессе расследования нашли цель
            if (_context.CurrentTarget != null)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            if (!_isAtLocation)
            {
                // Если добрались до места
                if (_context.NavigationAgent.IsNavigationFinished())
                {
                    _isAtLocation = true;
                    GD.Print($"[{_context.Name}] reached investigation point. Looking around...");
                }
            }
            else
            {
                // "Осматриваемся" на месте
                _timer -= delta;
                if (_timer <= 0)
                {
                    GD.Print($"[{_context.Name}] investigation complete. Nothing found.");
                    _context.ReturnToDefaultState();
                }
            }
        }
    }
}