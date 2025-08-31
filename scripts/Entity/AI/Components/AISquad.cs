using Godot;
using System.Collections.Generic;
using System.Linq;
using Game.Entity.AI.Orchestrator;
using Game.Entity.AI.States.Squad;

namespace Game.Entity.AI.Components
{
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
        [Export] public float PathWaypointThreshold { get; private set; } = 2f;

        [ExportGroup("Dynamic Behavior")]
        [Export] public float RepositionCheckInterval { get; private set; } = 4f;
        [Export] public float RepositionCooldown { get; private set; } = 1.5f;
        [Export] public float PursuitPredictionTime { get; private set; } = 2.2f;
        [Export] public float TargetVelocityTrackInterval { get; private set; } = 0.4f;
        [Export] public float TargetMovementThreshold { get; private set; } = 4f;
        [Export] public float OrientationUpdateInterval { get; private set; } = 0.3f;
        [Export] public float PursuitExitThresholdFactor { get; private set; } = 0.8f;

        // Public properties
        public readonly List<AIEntity> Members = [];
        public readonly HashSet<AIEntity> MembersAtDestination = [];
        public LivingEntity CurrentTarget { get; set; }
        public Vector3[] PathPoints { get; private set; }
        public int CurrentPathIndex { get; set; } = 0;
        public int PathDirection { get; set; } = 1;
        public bool IsInCombat => _currentState is CombatState || _currentState is PursuitState;

        public Vector3 ObservedTargetVelocity { get; set; } = Vector3.Zero;
        public Vector3 LastKnownTargetPosition { get; set; }
        public Vector3 TargetPreviousPosition { get; set; }

        private SquadStateBase _currentState;

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(SquadName))
            {
                GD.PushError($"AISquad '{Name}' has no SquadName assigned!");
                SetProcess(false);
                return;
            }
            Orchestrator.LegionBrain.Instance.RegisterSquad(this);

            // Подписываемся на события, которые касаются этого отряда
            AISignals.Instance.TargetEliminated += OnTargetEliminated;
            AISignals.Instance.RepositionRequested += OnRepositionRequested;
            AISignals.Instance.MemberDestroyed += OnMemberDestroyed;
        }

        public override void _ExitTree()
        {
            // Важно отписаться от событий при удалении ноды, чтобы избежать утечек памяти
            if (AISignals.Instance != null)
            {
                AISignals.Instance.TargetEliminated -= OnTargetEliminated;
                AISignals.Instance.RepositionRequested -= OnRepositionRequested;
                AISignals.Instance.MemberDestroyed -= OnMemberDestroyed;
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            _currentState?.Process(delta);
        }

        public void ChangeState(SquadStateBase newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            _currentState.Enter();
        }

        public void InitializeMembersFromGroup()
        {
            foreach (var node in GetTree().GetNodesInGroup(SquadName))
            {
                if (node is AIEntity ai)
                {
                    Members.Add(ai);
                    ai.AssignToSquad(this);
                }
            }
            GD.Print($"Squad '{SquadName}' initialized with {Members.Count} members.");

            if ((Task == SquadTask.PatrolPath || Task == SquadTask.AssaultPath) && MissionPath?.Curve.PointCount > 1)
            {
                PathPoints = MissionPath.Curve.GetBakedPoints();
                ChangeState(new PatrolState(this));
            }
            else
            {
                ChangeState(new IdleState(this));
            }
        }

        // Командные методы, которые инициируют смену состояний
        public void AssignMoveTarget(Vector3 targetPosition, Vector3? lookAtPosition = null)
        {
            ChangeState(new MoveToPointState(this, targetPosition, lookAtPosition));
        }

        public void AssignCombatTarget(LivingEntity target)
        {
            if (!IsInstanceValid(target) || (CurrentTarget == target && IsInCombat))
            {
                return;
            }

            if (Task == SquadTask.AssaultPath && _currentState is PatrolState)
            {
                GD.Print($"Squad '{SquadName}' is on assault task. Ignoring target {target.Name} to reach objective.");
                return;
            }

            if (CurrentTarget != target)
            {
                CurrentTarget = target;
                ObservedTargetVelocity = Vector3.Zero;
                LastKnownTargetPosition = target.GlobalPosition;
                TargetPreviousPosition = target.GlobalPosition;
            }

            ChangeState(new CombatState(this, target));
        }

        public void ReportPositionReached(AIEntity member)
        {
            MembersAtDestination.Add(member);
            // Дальнейшая логика (например, переход в Idle) теперь обрабатывается внутри состояния MoveToPointState
        }

        #region Event Handlers
        private void OnTargetEliminated(AIEntity reporter, LivingEntity eliminatedTarget)
        {
            if (!Members.Contains(reporter) || eliminatedTarget != CurrentTarget) return;

            GD.Print($"Squad '{SquadName}' confirms target {eliminatedTarget.Name} is eliminated. Disengaging...");
            Disengage();
        }

        private void OnRepositionRequested(AIEntity member)
        {
            if (!Members.Contains(member)) return;
            // Делегируем обработку запроса текущему состоянию
            (_currentState as IRepositionHandler)?.HandleRepositionRequest(member);
        }

        private void OnMemberDestroyed(AIEntity member)
        {
            if (!Members.Contains(member)) return;

            Members.Remove(member);
            MembersAtDestination.Remove(member);

            if (Members.Count == 0)
            {
                GD.Print($"Squad '{SquadName}' has been eliminated.");
                QueueFree(); // Отряд уничтожен
                return;
            }

            // Если были в бою, нужно переоценить тактику с учетом потерь
            if (IsInCombat && IsInstanceValid(CurrentTarget))
            {
                GD.Print($"Squad '{SquadName}' lost a member, re-evaluating combat tactics.");
                AssignCombatTarget(CurrentTarget);
            }
        }
        #endregion

        public void Disengage()
        {
            CurrentTarget = null;
            foreach (var member in Members) member.ClearOrders();

            if (Task == SquadTask.PatrolPath || Task == SquadTask.AssaultPath)
            {
                ChangeState(new PatrolState(this)); // Возобновляем патрулирование с текущей точки
            }
            else
            {
                ChangeState(new IdleState(this));
            }
        }

        public Vector3 GetSquadCenter()
        {
            if (Members.Count == 0) return Vector3.Zero;

            Vector3 center = Vector3.Zero;
            foreach (var member in Members)
            {
                center += member.GlobalPosition;
            }
            return center / Members.Count;
        }

        public void AssignMarchingFormationMove(Vector3 targetPosition, Vector3? lookAtPosition = null)
        {
            MembersAtDestination.Clear();
            CurrentTarget = null; // Движение отменяет боевую цель

            if (MarchingFormation == null || MarchingFormation.MemberPositions.Length == 0)
            {
                GD.PushWarning($"Squad '{SquadName}' has no MarchingFormation. Moving without formation.");
                foreach (var member in Members) member.ReceiveOrderMoveTo(targetPosition);
                return;
            }

            var squadCenter = GetSquadCenter(); // Используем уже оптимизированный метод
            var effectiveLookAt = lookAtPosition ?? targetPosition;
            var direction = squadCenter.IsEqualApprox(effectiveLookAt)
                ? (targetPosition - squadCenter).Normalized()
                : squadCenter.DirectionTo(effectiveLookAt).Normalized();

            if (direction.IsZeroApprox()) direction = Vector3.Forward;

            var rotation = Basis.LookingAt(direction, Vector3.Up);

            // Можно было бы и здесь использовать ListPool, но для разовой операции это не критично
            var worldPositions = new List<Vector3>();
            for (int i = 0; i < MarchingFormation.MemberPositions.Length; i++)
            {
                if (i >= Members.Count) break;
                var localOffset = MarchingFormation.MemberPositions[i];
                worldPositions.Add(targetPosition + (rotation * localOffset));
            }

            var assignments = AITacticalAnalysis.GetOptimalAssignments(Members, worldPositions, targetPosition);
            foreach (var (member, position) in assignments)
            {
                member.ReceiveOrderMoveTo(position);
            }
        }
    }
}