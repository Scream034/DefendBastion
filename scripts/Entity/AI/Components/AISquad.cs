using Godot;
using System.Collections.Generic;
using System.Linq;
using Game.Entity.AI.Orchestrator;

namespace Game.Entity.AI.Components
{
    public enum SquadState { Idle, MovingToPoint, FollowingPath, InCombat }
    public enum SquadTask { Standby, PatrolPath, AssaultPath }

    public partial class AISquad : Node
    {
        [ExportGroup("Configuration")]
        [Export] public string SquadName;
        [Export] public SquadTask Task = SquadTask.Standby;
        [Export] public Formation MarchingFormation;
        [Export] public Formation CombatFormation;
        [Export] public Path3D MissionPath;

        [ExportGroup("Task Settings")]
        [Export] private float _pathWaypointThreshold = 2.0f;

        public SquadState CurrentState { get; private set; } = SquadState.Idle;
        public LivingEntity CurrentTarget { get; private set; }

        private readonly List<AIEntity> _members = new();
        private readonly HashSet<AIEntity> _membersAtDestination = new();
        private Vector3 _squadCenterCache;

        private Vector3[] _pathPoints;
        private int _currentPathIndex = 0;
        private int _pathDirection = 1;

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(SquadName))
            {
                GD.PushError($"AISquad '{Name}' has no SquadName assigned!");
                SetProcess(false);
                return;
            }
            Orchestrator.LegionBrain.Instance.RegisterSquad(this);
            SetProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_members.Count == 0 || CurrentState != SquadState.FollowingPath) return;

            UpdateSquadCenter();
            var targetWaypoint = MissionPath.ToGlobal(_pathPoints[_currentPathIndex]);
            if (_squadCenterCache.DistanceSquaredTo(targetWaypoint) < _pathWaypointThreshold * _pathWaypointThreshold)
            {
                MoveToNextWaypoint();
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
                    Task = SquadTask.Standby;
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
                    CurrentState = SquadState.Idle;
                    return;
                }
            }
            else // PatrolPath
            {
                if ((_currentPathIndex >= _pathPoints.Length - 1 && _pathDirection > 0) || (_currentPathIndex <= 0 && _pathDirection < 0))
                {
                    _pathDirection *= -1;
                }
                // Pre-increment/decrement to avoid getting stuck at endpoints
                _currentPathIndex += _pathDirection;
            }

            var targetPosition = MissionPath.ToGlobal(_pathPoints[_currentPathIndex]);
            AssignMoveTarget(targetPosition);
            CurrentState = SquadState.FollowingPath;
        }


        public void AssignMoveTarget(Vector3 targetPosition)
        {
            GD.Print($"Squad '{SquadName}' moving to {targetPosition}.");
            CurrentState = SquadState.MovingToPoint;
            CurrentTarget = null;
            _membersAtDestination.Clear();

            if (MarchingFormation == null || MarchingFormation.MemberPositions.Length == 0)
            {
                GD.PushWarning($"Squad '{SquadName}' has no MarchingFormation. Moving without formation.");
                foreach (var member in _members) member.ReceiveOrderMoveTo(targetPosition);
                return;
            }

            UpdateSquadCenter();
            var direction = _members.Count > 0 ? (_squadCenterCache.DirectionTo(targetPosition)).Normalized() : Vector3.Forward;
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
            if (Task == SquadTask.AssaultPath && CurrentState == SquadState.FollowingPath)
            {
                GD.Print($"Squad '{SquadName}' is on assault task. Ignoring target {target.Name} to reach objective.");
                return;
            }

            if (CurrentTarget == target && CurrentState == SquadState.InCombat) return;

            CurrentTarget = target;
            CurrentState = SquadState.InCombat;
            _membersAtDestination.Clear();

            GD.Print($"Squad '{SquadName}' engaging target {target.Name}.");

            var positionAssignments = AITacticalAnalysis.FindCoverAndFirePositions(_members, target);

            if (positionAssignments == null || positionAssignments.Count == 0)
            {
                GD.Print("Squad '{SquadName}' failed to find any cover. Using CombatFormation as fallback.");
                // <--- ИЗМЕНЕНИЕ: Передаем цель в метод генерации, чтобы построиться относительно нее --->
                positionAssignments = GeneratePositionsFromFormation(CombatFormation, target);
            }

            if (positionAssignments != null && positionAssignments.Count > 0)
            {
                GD.Print($"Assigning {positionAssignments.Count} combat positions.");
                foreach (var (ai, position) in positionAssignments)
                {
                    ai.ReceiveOrderMoveTo(position);
                    ai.ReceiveOrderAttackTarget(target);
                }
            }
            else
            {
                GD.PushError($"Squad '{SquadName}' could not determine any combat positions for target {target.Name}!");
            }
        }

        // <--- ИЗМЕНЕНИЕ: Логика построения относительно цели --->
        private Dictionary<AIEntity, Vector3> GeneratePositionsFromFormation(Formation formation, LivingEntity target)
        {
            if (formation == null || formation.MemberPositions.Length == 0 || !IsInstanceValid(target)) return null;

            UpdateSquadCenter();

            // Якорь - это не наша текущая позиция, а точка на оптимальном расстоянии от врага!
            float optimalDistance = _members.Average(ai => ai.CombatBehavior.AttackRange) * 0.8f; // 80% от макс. дальности
            var directionFromTarget = target.GlobalPosition.DirectionTo(_squadCenterCache).Normalized();
            var anchorPoint = target.GlobalPosition + directionFromTarget * optimalDistance;

            // Поворачиваем формацию лицом к врагу
            var lookDirection = anchorPoint.DirectionTo(target.GlobalPosition).Normalized();
            var rotation = Basis.LookingAt(lookDirection, Vector3.Up);

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
            if (CurrentState != SquadState.MovingToPoint && CurrentState != SquadState.FollowingPath) return;

            _membersAtDestination.Add(member);
            if (CurrentState == SquadState.MovingToPoint && _membersAtDestination.Count >= _members.Count)
            {
                GD.Print($"Squad '{SquadName}' has reached its destination. Switching to Idle.");
                CurrentState = SquadState.Idle;
            }
        }

        public void OnMemberDestroyed(AIEntity member)
        {
            _members.Remove(member);
            _membersAtDestination.Remove(member);
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