using Godot;
using System.Collections.Generic;
using System.Linq;
using Game.Entity.AI.Behaviors;
using Game.Entity.AI.Orchestrator;

namespace Game.Entity.AI.Components
{
    // Расширяем SquadState
    public enum SquadState { Idle, MovingToPoint, FollowingPath, InCombat }
    // Добавляем основную задачу
    public enum SquadTask { Standby, PatrolPath, AssaultPath }


    public partial class AISquad : Node
    {
        [ExportGroup("Configuration")]
        [Export] public string SquadName;
        [Export] public SquadTask Task = SquadTask.Standby;
        [Export] public Formation MarchingFormation;
        [Export] public Formation CombatFormation;
        [Export] public Path3D MissionPath; // <--- НОВОЕ ПОЛЕ ДЛЯ ПУТИ

        [ExportGroup("Task Settings")]
        [Export] private float _pathWaypointThreshold = 2.0f; // Дистанция до точки пути для ее зачета

        public SquadState CurrentState { get; private set; } = SquadState.Idle;
        public LivingEntity CurrentTarget { get; private set; }

        private readonly List<AIEntity> _members = new();
        private readonly HashSet<AIEntity> _membersAtDestination = new();
        private Vector3 _squadCenterCache;

        // <--- НОВЫЕ ПОЛЯ ДЛЯ СЛЕДОВАНИЯ ПО ПУТИ --->
        private Vector3[] _pathPoints;
        private int _currentPathIndex = 0;
        private int _pathDirection = 1; // 1 для вперед, -1 для назад (в патруле)

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(SquadName))
            {
                GD.PushError($"AISquad '{Name}' has no SquadName assigned!");
                SetProcess(false);
                return;
            }
            Orchestrator.LegionBrain.Instance.RegisterSquad(this);
            SetProcess(true); // Включаем process-цикл для автономного поведения
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_members.Count == 0) return;

            // Автономное поведение отряда
            if (CurrentState == SquadState.FollowingPath)
            {
                UpdateSquadCenter();
                var targetWaypoint = MissionPath.ToGlobal(_pathPoints[_currentPathIndex]);
                if (_squadCenterCache.DistanceSquaredTo(targetWaypoint) < _pathWaypointThreshold * _pathWaypointThreshold)
                {
                    MoveToNextWaypoint();
                }
            }
        }

        public void InitializeMembersFromGroup()
        {
            foreach (var node in GetTree().GetNodesInGroup(SquadName))
            {
                if (node is AIEntity ai)
                {
                    _members.Add(ai);
                    ai.AssignToSquad(this);
                }
            }
            GD.Print($"Squad '{SquadName}' initialized with {_members.Count} members.");

            // <--- ИНИЦИАЛИЗАЦИЯ ЗАДАЧИ --->
            if (Task == SquadTask.PatrolPath || Task == SquadTask.AssaultPath)
            {
                if (MissionPath != null && MissionPath.Curve.PointCount > 0)
                {
                    _pathPoints = MissionPath.Curve.GetBakedPoints();
                    StartFollowingPath();
                }
                else
                {
                    GD.PushWarning($"Squad '{SquadName}' is set to follow a path, but MissionPath is not assigned or is empty.");
                    Task = SquadTask.Standby; // Переводим в безопасный режим
                }
            }
        }

        private void StartFollowingPath()
        {
            GD.Print($"Squad '{SquadName}' starts following its mission path.");
            CurrentState = SquadState.FollowingPath;
            _currentPathIndex = 0;
            MoveToNextWaypoint();
        }

        private void MoveToNextWaypoint()
        {
            if (Task == SquadTask.AssaultPath)
            {
                if (_currentPathIndex >= _pathPoints.Length)
                {
                    GD.Print($"Squad '{SquadName}' has completed its assault path.");
                    CurrentState = SquadState.Idle; // Штурм завершен, ждем новых приказов
                    return;
                }
            }
            else // PatrolPath
            {
                if ((_currentPathIndex >= _pathPoints.Length - 1 && _pathDirection > 0) || (_currentPathIndex <= 0 && _pathDirection < 0))
                {
                    _pathDirection *= -1; // Меняем направление в конце пути
                }
                _currentPathIndex += _pathDirection;
            }

            var targetPosition = MissionPath.ToGlobal(_pathPoints[_currentPathIndex]);
            AssignMoveTarget(targetPosition); // Используем уже существующий метод для движения в строю
            CurrentState = SquadState.FollowingPath; // Перезаписываем состояние, так как AssignMoveTarget ставит MovingToPoint
        }

        public void AssignMoveTarget(Vector3 targetPosition)
        {
            GD.Print($"Squad '{SquadName}' moving to {targetPosition}.");
            CurrentState = SquadState.MovingToPoint;
            CurrentTarget = null;
            _membersAtDestination.Clear();

            if (MarchingFormation == null || MarchingFormation.MemberPositions.Length == 0)
            {
                foreach (var member in _members) member.ReceiveOrderMoveTo(targetPosition);
                return;
            }

            UpdateSquadCenter();
            var direction = (_squadCenterCache.DirectionTo(targetPosition)).Normalized();
            var rotation = Basis.LookingAt(direction, Vector3.Up);

            for (int i = 0; i < _members.Count; i++)
            {
                if (i >= MarchingFormation.MemberPositions.Length) break;
                var localOffset = MarchingFormation.MemberPositions[i];
                var worldOffset = rotation * localOffset;
                var memberTargetPosition = targetPosition + worldOffset;
                _members[i].ReceiveOrderMoveTo(memberTargetPosition);
            }
        }

        public void AssignCombatTarget(LivingEntity target)
        {
            // <--- ИЗМЕНЕНИЕ: ПРОВЕРКА ЗАДАЧИ "ШТУРМ" --->
            if (Task == SquadTask.AssaultPath && CurrentState == SquadState.FollowingPath)
            {
                GD.Print($"Squad '{SquadName}' is on assault task. Ignoring target {target.Name} to reach objective.");
                return; // Игнорируем цели, пока не дойдем до конца пути штурма
            }

            if (CurrentTarget == target && CurrentState == SquadState.InCombat) return;

            CurrentTarget = target;
            CurrentState = SquadState.InCombat;
            _membersAtDestination.Clear();

            GD.Print($"Squad '{SquadName}' engaging target {target.Name}.");

            var positionAssignments = AITacticalAnalysis.FindCoverAndFirePositions(_members, target);

            if (positionAssignments == null || positionAssignments.Count < _members.Count)
            {
                GD.PushWarning($"Squad '{SquadName}' failed to find enough cover. Using CombatFormation as fallback.");
                positionAssignments = GeneratePositionsFromFormation(CombatFormation, target.GlobalPosition);
            }

            if (positionAssignments != null)
            {
                foreach (var (ai, position) in positionAssignments)
                {
                    ai.ReceiveOrderMoveTo(position);
                    ai.ReceiveOrderAttackTarget(target);
                }
            }
        }

        private Dictionary<AIEntity, Vector3> GeneratePositionsFromFormation(Formation formation, Vector3 faceTowards)
        {
            if (formation == null || formation.MemberPositions.Length == 0) return null;

            UpdateSquadCenter();
            var direction = (_squadCenterCache.DirectionTo(faceTowards)).Normalized();
            var rotation = Basis.LookingAt(direction, Vector3.Up);
            var anchorPoint = _squadCenterCache;

            var assignments = new Dictionary<AIEntity, Vector3>();
            for (int i = 0; i < _members.Count; i++)
            {
                if (i >= formation.MemberPositions.Length) break;
                var localOffset = formation.MemberPositions[i];
                var worldOffset = rotation * localOffset;
                var navMap = _members[i].GetWorld3D().NavigationMap;
                var targetPos = NavigationServer3D.MapGetClosestPoint(navMap, anchorPoint + worldOffset);
                assignments[_members[i]] = targetPos;
            }
            return assignments;
        }

        public void ReportPositionReached(AIEntity member)
        {
            if (CurrentState != SquadState.MovingToPoint) return;

            _membersAtDestination.Add(member);
            if (_membersAtDestination.Count >= _members.Count)
            {
                GD.Print($"Squad '{SquadName}' has reached its destination. Switching to Idle.");
                CurrentState = SquadState.Idle;
            }
        }

        public void OnMemberDestroyed(AIEntity member)
        {
            _members.Remove(member);
            _membersAtDestination.Remove(member); // Важно удалить и из этого списка
            if (_members.Count == 0)
            {
                GD.Print($"Squad '{SquadName}' has been eliminated.");
                QueueFree();
            }
            else if (CurrentState == SquadState.InCombat && IsInstanceValid(CurrentTarget))
            {
                AssignCombatTarget(CurrentTarget);
            }
        }

        private void UpdateSquadCenter()
        {
            if (_members.Count == 0) return;
            _squadCenterCache = _members.Select(m => m.GlobalPosition).Aggregate(Vector3.Zero, (a, b) => a + b) / _members.Count;
        }
    }
}