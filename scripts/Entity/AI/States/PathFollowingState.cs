using Godot;

namespace Game.Entity.AI.States
{
    public sealed class PathFollowingState(AIEntity context) : State(context)
    {
        private Vector3[] _pathPoints;
        private int _currentPointIndex = 0;
        private int _pathDirection = 1;

        public override void Enter()
        {
            if (_context.Profile.MissionPath == null)
            {
                _context.ReturnToDefaultState();
                return;
            }

            _pathPoints = _context.Profile.MissionPath.Curve.GetBakedPoints();
            if (_pathPoints == null || _pathPoints.Length == 0)
            {
                _context.ReturnToDefaultState();
                return;
            }

            var speed = (_context.Profile.MainTask == AIMainTask.Assault && _context.Profile.AssaultBehavior == AssaultMode.Rush)
                ? _context.Profile.MovementProfile.FastSpeed
                : _context.Profile.MovementProfile.NormalSpeed;
            _context.SetMovementSpeed(speed);

            MoveToNextPoint();
        }

        public override void Update(float delta)
        {
            if (_context.TargetingSystem.CurrentTarget != null && _context.Profile.AssaultBehavior != AssaultMode.Rush)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            if (_context.MovementController.NavigationAgent.IsNavigationFinished())
            {
                MoveToNextPoint();
            }
        }

        private void MoveToNextPoint()
        {
            if (_context.Profile.MainTask == AIMainTask.Assault)
            {
                _currentPointIndex++;
                if (_currentPointIndex >= _pathPoints.Length)
                {
                    _context.MovementController.StopMovement(); 
                    return;
                }
            }
            else // PathPatrol
            {
                if ((_currentPointIndex >= _pathPoints.Length - 1 && _pathDirection > 0) || (_currentPointIndex <= 0 && _pathDirection < 0))
                {
                    _pathDirection *= -1;
                }
                _currentPointIndex += _pathDirection;
            }
            
            var targetPoint = _context.Profile.MissionPath.ToGlobal(_pathPoints[_currentPointIndex]);
            _context.MovementController.MoveTo(targetPoint);
        }
    }
}