using Godot;
using Game.Entity.AI.Profiles;

namespace Game.Entity.AI.Components
{
    public partial class AILookController : Node
    {
        private AIEntity _context;
        private AILookProfile _profile;

        private Vector3? _interestPoint;
        private float _glanceTimer;
        private bool _isGlancingAtInterestPoint;

        public Vector3 CurrentLookTarget { get; private set; }

        public void Initialize(AIEntity context)
        {
            _context = context;
            _profile = _context.Profile?.LookProfile;
            if (_profile == null)
            {
                GD.PushError($"AILookProfile is not set for {_context.Name}. Look controller will not work.");
                SetProcess(false);
            }
        }

        public override void _Process(double delta)
        {
            Vector3 lookDirection = _context.Velocity.IsZeroApprox() ? _context.Basis.Z : _context.Velocity.Normalized();
            Vector3 finalLookPosition = _context.GlobalPosition + lookDirection * 10f;

            var currentTarget = _context.TargetingSystem.CurrentTarget;

            if (GodotObject.IsInstanceValid(currentTarget))
            {
                finalLookPosition = currentTarget.GlobalPosition;
            }
            else if (_profile.LookAtInterestPointWhileMoving && _interestPoint.HasValue && _context.IsMoving)
            {
                _glanceTimer -= (float)delta;
                if (_glanceTimer <= 0f)
                {
                    _isGlancingAtInterestPoint = !_isGlancingAtInterestPoint;
                    _glanceTimer = _isGlancingAtInterestPoint
                        ? (float)GD.RandRange(_profile.MinGlanceDuration, _profile.MaxGlanceDuration)
                        : (float)GD.RandRange(_profile.MinLookForwardDuration, _profile.MaxLookForwardDuration);
                }

                if (_isGlancingAtInterestPoint)
                {
                    finalLookPosition = _interestPoint.Value;
                }
            }

            CurrentLookTarget = finalLookPosition;
        }

        public void SetInterestPoint(Vector3? targetPosition)
        {
            _interestPoint = targetPosition;
            _glanceTimer = 0;
            _isGlancingAtInterestPoint = true;
            GD.Print($"{_context.Name} setting look interest point to: {targetPosition?.ToString() ?? "None"}");
        }
    }
}