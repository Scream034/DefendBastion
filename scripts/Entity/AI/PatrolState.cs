using Godot;

namespace Game.Entity.AI;

/// <summary>
/// Состояние патрулирования. ИИ движется к случайной достижимой точке
/// в пределах заданного радиуса от своей точки спавна, ждет некоторое время,
/// а затем выбирает новую точку.
/// </summary>
public sealed class PatrolState : State
{
    private enum SubState
    {
        Moving,
        Waiting
    }

    private SubState _currentSubState;
    private float _waitTimer;

    public PatrolState(AIEntity context) : base(context) { }

    public override void Enter()
    {
        GD.Print($"{_context.Name} входит в состояние Patrol.");
        _currentSubState = SubState.Moving;
        FindAndMoveToNewPatrolPoint();
    }

    public override void Update(float delta)
    {
        // Главный приоритет: если появилась цель, немедленно переходим в атаку.
        if (_context.CurrentTarget != null)
        {
            _context.ChangeState(new AttackState(_context));
            return;
        }

        switch (_currentSubState)
        {
            case SubState.Moving:
                // Если мы дошли до цели...
                if (_context.NavigationAgent.IsNavigationFinished())
                {
                    // ...переключаемся в режим ожидания.
                    _currentSubState = SubState.Waiting;
                    SetWaitTimer();
                    GD.Print($"{_context.Name} достиг точки патрулирования, ждет {_waitTimer:0.0} сек.");
                }
                break;

            case SubState.Waiting:
                _waitTimer -= delta;
                // Если время ожидания вышло...
                if (_waitTimer <= 0)
                {
                    // ...ищем новую точку и начинаем движение.
                    _currentSubState = SubState.Moving;
                    FindAndMoveToNewPatrolPoint();
                }
                break;
        }
    }

    public override void Exit()
    {
        // При выходе из состояния патрулирования (например, для атаки)
        // мы останавливаем текущее движение, чтобы избежать "скольжения"
        // к последней патрульной точке.
        _context.StopMovement();
        GD.Print($"{_context.Name} выходит из состояния Patrol.");
    }

    /// <summary>
    /// Устанавливает таймер ожидания в соответствии с настройками в AIEntity.
    /// </summary>
    private void SetWaitTimer()
    {
        if (_context.UseRandomWaitTime)
        {
            _waitTimer = (float)GD.RandRange(_context.MinPatrolWaitTime, _context.MaxPatrolWaitTime);
        }
        else
        {
            _waitTimer = _context.MaxPatrolWaitTime; // Используем максимальное значение как фиксированное
        }
    }

    /// <summary>
    /// Находит случайную точку на NavMesh в пределах радиуса патрулирования и
    /// дает команду AIEntity двигаться к ней.
    /// </summary>
    private void FindAndMoveToNewPatrolPoint()
    {
        float radius;

        // Определяем радиус поиска в зависимости от настроек
        if (_context.UseRandomPatrolRadius)
        {
            radius = (float)GD.RandRange(_context.MinPatrolRadius, _context.MaxPatrolRadius);
        }
        else
        {
            radius = _context.MaxPatrolRadius;
        }

        // 1. Генерируем случайную точку внутри 2D-окружности
        var randomDirection = Vector2.FromAngle((float)GD.RandRange(0, Mathf.Tau)).Normalized();
        var targetPoint = _context.SpawnPosition + new Vector3(randomDirection.X, 0, randomDirection.Y) * radius;

        // 2. Находим ближайшую к этой случайной точке валидную точку на навигационной сетке
        var navMap = _context.GetWorld3D().NavigationMap;
        var reachablePoint = NavigationServer3D.MapGetClosestPoint(navMap, targetPoint);

        GD.Print($"{_context.Name} получил новую точку патрулирования: {reachablePoint}");
        _context.MoveTo(reachablePoint);
    }
}