using Game.Entity.AI.Profiles;
using Godot;

namespace Game.Entity.AI.States;

/// <summary>
/// Состояние патрулирования. ИИ движется к случайной достижимой точке
/// в пределах заданного радиуса от своей точки спавна, ждет некоторое время,
/// а затем выбирает новую точку.
/// </summary>
public sealed class PatrolState(AIEntity context) : State(context)
{
    private enum SubState
    {
        Moving,
        Waiting
    }

    private SubState _currentSubState;
    private float _waitTimer;
    private readonly AIPatrolProfile _profile = context.Profile.PatrolProfile;

    public override void Enter()
    {
        _context.SetMovementSpeed(_context.Profile.MovementProfile.NormalSpeed);
        _currentSubState = SubState.Moving;
        FindAndMoveToNewPatrolPoint();
    }


    public override void Exit()
    {
        _context.MovementController.StopMovement();
    }

    public override void Update(float delta)
    {
        if (_context.TargetingSystem.CurrentTarget != null)
        {
            _context.ChangeState(new AttackState(_context));
            return;
        }

        switch (_currentSubState)
        {
            case SubState.Moving:
                if (_context.MovementController.NavigationAgent.IsNavigationFinished())
                {
                    _currentSubState = SubState.Waiting;
                    SetWaitTimer();
                }
                break;
            case SubState.Waiting:
                _waitTimer -= delta;
                if (_waitTimer <= 0)
                {
                    _currentSubState = SubState.Moving;
                    FindAndMoveToNewPatrolPoint();
                }
                break;
        }
    }

    /// <summary>
    /// Устанавливает таймер ожидания в соответствии с настройками в AIEntity.
    /// </summary>
    private void SetWaitTimer()
    {
        _waitTimer = _profile.UseRandomWaitTime
            ? (float)GD.RandRange(_profile.MinPatrolWaitTime, _profile.MaxPatrolWaitTime)
            : _profile.MaxPatrolWaitTime;
    }

    /// <summary>
    /// Находит случайную точку на NavMesh в пределах радиуса патрулирования и
    /// дает команду AIEntity двигаться к ней.
    /// </summary>
    private void FindAndMoveToNewPatrolPoint()
    {
        float radius = _profile.UseRandomPatrolRadius
            ? (float)GD.RandRange(_profile.MinPatrolRadius, _profile.MaxPatrolRadius)
            : _profile.MaxPatrolRadius;

        var randomDirection = Vector2.FromAngle((float)GD.RandRange(0, Mathf.Tau));
        var targetPoint = _context.SpawnPosition + new Vector3(randomDirection.X, 0, randomDirection.Y) * radius;

        var navMap = _context.GetWorld3D().NavigationMap;
        var reachablePoint = NavigationServer3D.MapGetClosestPoint(navMap, targetPoint);

        _context.MovementController.MoveTo(reachablePoint);
        _context.LookController.SetInterestPoint(reachablePoint);
    }
}