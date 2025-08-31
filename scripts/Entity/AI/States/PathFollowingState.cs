using Godot;
using System;

namespace Game.Entity.AI.States
{
    public sealed class PathFollowingState(AIEntity context) : State(context)
    {
        private Vector3[] _pathPoints;
        private int _currentPointIndex = 0;
        private int _pathDirection = 1;

        public override void Enter()
        {
            if (_context.MissionPath == null)
            {
                _context.ReturnToDefaultState();
                return;
            }

            _pathPoints = _context.MissionPath.Curve.GetBakedPoints();
            if (_pathPoints == null || _pathPoints.Length == 0)
            {
                _context.ReturnToDefaultState();
                return;
            }

            var speed = (_context.MainTask == AIMainTask.Assault && _context.AssaultBehavior == AssaultMode.Rush)
                ? _context.Profile.MovementProfile.FastSpeed
                : _context.Profile.MovementProfile.NormalSpeed;
            _context.SetMovementSpeed(speed);

            MoveToNextPoint();
        }

        public override void Update(float delta)
        {
            if (_context.TargetingSystem.CurrentTarget != null && _context.AssaultBehavior != AssaultMode.Rush)
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
            if (_context.MainTask == AIMainTask.Assault)
            {
                _currentPointIndex++;
                if (_currentPointIndex >= _pathPoints.Length)
                {
                    _context.MovementController.StopMovement();
                    _context.OnMissionPathCompleted(); // Сообщаем AI, что он дошел до конца пути
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

            // Получаем идеальную точку с пути
            var idealTargetPoint = _context.MissionPath.ToGlobal(_pathPoints[_currentPointIndex]);
            var finalTargetPoint = idealTargetPoint;

            var patrolProfile = _context.Profile?.PatrolProfile;
            if (patrolProfile != null && patrolProfile.PathWaypointAccuracyRadius > 0.0f)
            {
                // Генерируем случайное смещение на плоскости XZ
                var randomAngle = (float)Random.Shared.NextDouble() * Mathf.Pi * 2f;
                var randomDirection = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle));
                var randomDistance = (float)Random.Shared.NextDouble() * patrolProfile.PathWaypointAccuracyRadius;
                var offset = new Vector3(randomDirection.X, 0, randomDirection.Y) * randomDistance;

                finalTargetPoint += offset;
            }

            // Находим ближайшую достижимую точку на NavMesh к нашей "неточной" цели.
            var navMap = _context.GetWorld3D().NavigationMap;
            var reachablePoint = NavigationServer3D.MapGetClosestPoint(navMap, finalTargetPoint);

            _context.MovementController.MoveTo(reachablePoint);
        }
    }
}