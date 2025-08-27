using Godot;

namespace Game.Entity.AI.States
{
    /// <summary>
    /// Состояние для следования по заданному пути (Path3D).
    /// Используется для задач PathPatrol и Assault.
    /// </summary>
    public sealed class PathFollowingState(AIEntity context) : State(context)
    {
        private Vector3[] _pathPoints;
        private int _currentPointIndex = 0;
        private int _pathDirection = 1; // 1 for forward, -1 for backward (for patrol)

        public override void Enter()
        {
            if (_context.MissionPath == null)
            {
                GD.PushError($"[{_context.Name}] entered PathFollowingState with no MissionPath!");
                _context.ReturnToDefaultState();
                return;
            }

            _pathPoints = _context.MissionPath.Curve.GetBakedPoints();
            if (_pathPoints == null || _pathPoints.Length == 0)
            {
                GD.PushError($"[{_context.Name}] MissionPath has no points!");
                _context.ReturnToDefaultState();
                return;
            }

            // Устанавливаем скорость в зависимости от задачи
            if (_context.MainTask == AIMainTask.Assault && _context.AssaultBehavior == AssaultMode.Rush)
            {
                _context.SetMovementSpeed(_context.FastSpeed);
            }
            else
            {
                _context.SetMovementSpeed(_context.NormalSpeed); // Нормальная для штурма и патруля
            }

            MoveToNextPoint();
        }

        public override void Update(float delta)
        {
            // Если появилась цель и мы не в режиме "Rush", то атакуем
            if (_context.CurrentTarget != null && _context.AssaultBehavior != AssaultMode.Rush)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            if (_context.NavigationAgent.IsNavigationFinished())
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
                    GD.Print($"[{_context.Name}] Assault mission complete!");
                    // Можно добавить логику "удержания точки" или просто патрулирования
                    _context.StopMovement(); 
                    return;
                }
            }
            else // PathPatrol
            {
                if ((_currentPointIndex >= _pathPoints.Length - 1 && _pathDirection > 0) ||
                    (_currentPointIndex <= 0 && _pathDirection < 0))
                {
                    _pathDirection *= -1; // Разворачиваемся на концах пути
                }
                _currentPointIndex += _pathDirection;
            }
            
            // Получаем глобальную позицию точки пути
            var targetPoint = _context.MissionPath.ToGlobal(_pathPoints[_currentPointIndex]);
            _context.MoveTo(targetPoint);
        }
    }
}