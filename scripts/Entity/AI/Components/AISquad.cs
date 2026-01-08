#nullable enable

using Godot;
using System.Collections.Generic;
using Game.Entity.AI.Orchestrator;
using Game.Entity.AI.States.Squad;

namespace Game.Entity.AI.Components
{
    public enum SquadTask { Standby, PatrolPath, AssaultPath }
    /// <summary>
    /// Координатор группы AI. Управляет состояниями, формациями и общими целями.
    /// </summary>
    public partial class AISquad : Node
    {
        [ExportGroup("Configuration")]
        [Export] public SquadTask Task = SquadTask.Standby;
        [Export] public Formation? MarchingFormation;
        [Export] public Formation? CombatFormation;
        [Export] public Path3D? MissionPath;

        /// <summary>
        /// Если true, отряд автоматически подхватывает AIEntity, добавленные в дерево как дети.
        /// </summary>
        [Export] public bool IsMembershipDynamic { get; private set; } = false;

        [ExportGroup("Task Settings")]
        [Export] public float PathWaypointThreshold { get; private set; } = 2f;

        [ExportGroup("Dynamic Behavior")]
        [Export] public float RepositionCheckInterval { get; private set; } = 4f;
        [Export] public float RepositionCooldown { get; private set; } = 1.5f;
        [Export] public float PursuitPredictionTime { get; private set; } = 2.2f;
        [Export] public float TargetVelocityTrackInterval { get; private set; } = 0.4f;
        [Export] public float TargetMovementThreshold { get; private set; } = 4f;
        [Export] public float OrientationUpdateInterval { get; private set; } = 0.3f;
        [Export] public float PursuitExitThresholdFactor { get; private set; } = 0.8f;

        [ExportGroup("Pursuit & Search")]
        [Export] public bool CanPursueTarget { get; private set; } = true;

        /// <summary>
        /// Время (сек), которое отряд ищет врага в последней известной точке перед уходом.
        /// </summary>
        [Export] public float SearchDuration { get; private set; } = 6.0f;

        /// <summary>
        /// Максимальная дистанция от центра отряда до цели. Если враг дальше - абсолютный сброс.
        /// Служит предохранителем от багов.
        /// </summary>
        [Export] public float MaxPursuitDistance { get; private set; } = 150.0f;

        /// <summary>
        /// Максимальное время преследования без результата.
        /// </summary>
        [Export] public float PursuitGiveUpTime { get; private set; } = 15.0f;


        // Public Runtime Data
        public readonly List<AIEntity> Members = [];
        public readonly HashSet<AIEntity> MembersAtDestination = [];
        public LivingEntity? CurrentTarget { get; set; }
        public Vector3[] PathPoints { get; private set; } = [];
        public int CurrentPathIndex { get; set; } = 0;
        public int PathDirection { get; set; } = 1;
        public bool IsInCombat => CurrentState is CombatState || CurrentState is PursuitState;

        public Vector3 ObservedTargetVelocity { get; set; } = Vector3.Zero;
        public Vector3 LastKnownTargetPosition { get; set; }
        public Vector3 TargetPreviousPosition { get; set; }

        public SquadStateBase? CurrentState { get; private set; }

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(Name))
            {
                GD.PushError($"AISquad '{Name}' has no Name assigned!");
                SetProcess(false);
                return;
            }
            LegionBrain.Instance?.RegisterSquad(this);

            AISignals.Instance.TargetEliminated += OnTargetEliminated;
            AISignals.Instance.RepositionRequested += OnRepositionRequested;
            AISignals.Instance.MemberDestroyed += OnMemberDestroyed;

            if (IsMembershipDynamic)
            {
                ChildEnteredTree += OnMemberNodeAdded;
                ChildExitingTree += OnMemberNodeRemoved;
            }
        }

        public override void _ExitTree()
        {
            if (AISignals.Instance != null)
            {
                AISignals.Instance.TargetEliminated -= OnTargetEliminated;
                AISignals.Instance.RepositionRequested -= OnRepositionRequested;
                AISignals.Instance.MemberDestroyed -= OnMemberDestroyed;
            }

            if (IsMembershipDynamic)
            {
                ChildEnteredTree -= OnMemberNodeAdded;
                ChildExitingTree -= OnMemberNodeRemoved;
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            CurrentState?.Process(delta);
        }

        public void ChangeState(SquadStateBase newState)
        {
            CurrentState?.Exit();
            CurrentState = newState;
            CurrentState.Enter();
        }

        /// <summary>
        /// Проверяет, находится ли цель в сенсорной зоне ХОТЯ БЫ У ОДНОГО члена отряда.
        /// </summary>
        public bool IsTargetInSensorRange()
        {
            if (!IsInstanceValid(CurrentTarget)) return false;
            foreach (var member in Members)
            {
                if (member.TargetingSystem.IsTargetInSensorRange(CurrentTarget))
                    return true;
            }
            return false;
        }

        public void InitializeMembers()
        {
            Members.Clear();
            foreach (var node in GetChildren())
            {
                if (node is AIEntity ai)
                {
                    Members.Add(ai);
                    ai.AssignToSquad(this);
                }
            }
            GD.Print($"Squad '{Name}' initialized with {Members.Count} members.");

            if ((Task is SquadTask.PatrolPath or SquadTask.AssaultPath) && MissionPath?.Curve.PointCount > 1)
            {
                PathPoints = MissionPath.Curve.GetBakedPoints();
                ChangeState(new PatrolState(this));
            }
            else
            {
                ChangeState(new IdleState(this));
            }
        }

        public void AssignMoveTarget(Vector3 targetPosition, Vector3? lookAtPosition = null)
        {
            ChangeState(new MoveToPointState(this, targetPosition, lookAtPosition));
        }

        public void AssignCombatTarget(LivingEntity target)
        {
            if (!IsInstanceValid(target)) return;

            if (Task == SquadTask.AssaultPath)
            {
                // В режиме штурма не меняем стейт, просто приказываем стрелять
                foreach (var member in Members) member.ReceiveOrderAttackTarget(target);
                return;
            }

            if (CurrentTarget != target)
            {
                CurrentTarget = target;
                ObservedTargetVelocity = Vector3.Zero;
                LastKnownTargetPosition = target.GlobalPosition;
                TargetPreviousPosition = target.GlobalPosition;
            }

            if (!IsInCombat)
            {
                ChangeState(new CombatState(this, target));
            }
        }

        public void Disengage()
        {
            CurrentTarget = null;
            foreach (var member in Members) member.ClearOrders();

            if (Task is SquadTask.PatrolPath or SquadTask.AssaultPath)
                ChangeState(new PatrolState(this));
            else
                ChangeState(new IdleState(this));
        }

        public void ReportPositionReached(AIEntity member) => MembersAtDestination.Add(member);

        public Vector3 GetSquadCenter()
        {
            if (Members.Count == 0) return Vector3.Zero;
            Vector3 center = Vector3.Zero;
            foreach (var member in Members) center += member.GlobalPosition;
            return center / Members.Count;
        }

        public void AssignMarchingFormationMove(Vector3 targetPosition, Vector3? lookAtPosition = null)
        {
            MembersAtDestination.Clear();
            CurrentTarget = null;

            if (MarchingFormation == null || MarchingFormation.MemberPositions.Length == 0)
            {
                foreach (var member in Members) member.ReceiveOrderMoveTo(targetPosition);
                return;
            }

            var squadCenter = GetSquadCenter();
            var effectiveLookAt = lookAtPosition ?? targetPosition;
            var direction = squadCenter.IsEqualApprox(effectiveLookAt)
                ? (targetPosition - squadCenter).Normalized()
                : squadCenter.DirectionTo(effectiveLookAt).Normalized();

            if (direction.IsZeroApprox()) direction = Vector3.Forward;

            var rotation = Basis.LookingAt(direction, Vector3.Up);
            var worldPositions = new List<Vector3>();
            for (int i = 0; i < MarchingFormation.MemberPositions.Length; i++)
            {
                if (i >= Members.Count) break;
                worldPositions.Add(targetPosition + (rotation * MarchingFormation.MemberPositions[i]));
            }

            var assignments = AITacticalAnalysis.GetOptimalAssignments(Members, worldPositions, targetPosition);
            foreach (var (member, position) in assignments)
            {
                member.ReceiveOrderMoveTo(position);
            }
        }

        #region Event Handlers
        private void OnTargetEliminated(AIEntity reporter, LivingEntity eliminatedTarget)
        {
            if (!Members.Contains(reporter) || eliminatedTarget != CurrentTarget) return;
            Disengage();
        }

        private void OnRepositionRequested(AIEntity member)
        {
            if (!Members.Contains(member)) return;
            (CurrentState as IRepositionHandler)?.HandleRepositionRequest(member);
        }

        private void OnMemberDestroyed(AIEntity member) => HandleMemberRemoval(member, "destroyed");

        private void OnMemberNodeAdded(Node node)
        {
            if (node is AIEntity newMember && !Members.Contains(newMember))
            {
                Members.Add(newMember);
                newMember.AssignToSquad(this);
                if (IsInCombat && CurrentTarget != null) newMember.ReceiveOrderAttackTarget(CurrentTarget);
            }
        }

        private void OnMemberNodeRemoved(Node node)
        {
            if (node is AIEntity member) HandleMemberRemoval(member, "removed from tree");
        }

        private void HandleMemberRemoval(AIEntity member, string reason)
        {
            if (!Members.Contains(member)) return;
            Members.Remove(member);
            MembersAtDestination.Remove(member);
            if (Members.Count == 0) QueueFree();
            else if (IsInCombat && IsInstanceValid(CurrentTarget)) AssignCombatTarget(CurrentTarget);
        }
        #endregion
    }
}