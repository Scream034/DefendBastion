#nullable enable

using Godot;
using System.Collections.Generic;
using Game.Entity.AI.Orchestrator;
using Game.Entity.AI.States.Squad;

namespace Game.Entity.AI.Components
{
    public enum SquadTask { Standby, PatrolPath, AssaultPath }

    public partial class AISquad : Node
    {
        [ExportGroup("Configuration")]
        [Export] public SquadTask Task = SquadTask.Standby;
        [Export] public Formation? MarchingFormation;
        [Export] public Formation? CombatFormation;
        [Export] public Path3D? MissionPath;

        /// <summary>
        /// Если true, отряд будет автоматически отслеживать добавление и удаление
        /// дочерних узлов AIEntity в реальном времени.
        /// По умолчанию false, что означает, что состав определяется один раз при запуске.
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

        // Public properties
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

            // Подписка на сигналы дерева для динамического управления ---
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

            // Отписка от сигналов дерева
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
        /// Инициализирует состав отряда, находя всех дочерних AIEntity.
        /// Этот метод вызывается из LegionBrain после того, как все ноды будут в сцене.
        /// </summary>
        public void InitializeMembers()
        {
            // На всякий случай очищаем список, если этот метод будет вызван повторно.
            Members.Clear();

            foreach (var node in GetChildren())
            {
                if (node is AIEntity ai)
                {
                    Members.Add(ai);
                    ai.AssignToSquad(this);
                }
            }
            GD.Print($"Squad '{Name}' initialized with {Members.Count} members from its children.");

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

        // Командные методы, которые инициируют смену состояний
        public void AssignMoveTarget(Vector3 targetPosition, Vector3? lookAtPosition = null)
        {
            ChangeState(new MoveToPointState(this, targetPosition, lookAtPosition));
        }

        public void AssignCombatTarget(LivingEntity target)
        {
            if (!IsInstanceValid(target)) return;

            // 1. ФИЛЬТР: Режим Штурма
            if (Task == SquadTask.AssaultPath)
            {
                // В режиме штурма мы игнорируем переключение в режим боя (CombatState).
                // Исключение: если цель стоит прямо на пути (можно проверить дистанцию и угол),
                // но в рамках задачи "плевать на всё" мы просто продолжаем бежать.
                // Бойцы могут стрелять на ходу (Firing Envelope), если цель попадет в прицел,
                // но сам Сквад не должен менять стейт на CombatState, так как это остановит движение.

                // Если мы очень хотим, чтобы они стреляли на бегу, мы можем передать цель
                // бойцам индивидуально, не меняя стейт отряда.
                foreach (var member in Members)
                {
                    // Даем приказ атаковать, но НЕ меняем стейт отряда на Combat.
                    // Бойцы будут пытаться стрелять в _PhysicsProcess, если цель в секторе,
                    // но движение останется приоритетом из PatrolState.
                    member.ReceiveOrderAttackTarget(target);
                }

                GD.Print($"Squad '{Name}' ignores engagement rule due to ASSAULT mode. Ordering fire-at-will while moving.");
                return;
            }

            // Стандартная логика
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

        public void ReportPositionReached(AIEntity member)
        {
            MembersAtDestination.Add(member);
        }

        #region Event Handlers
        private void OnTargetEliminated(AIEntity reporter, LivingEntity eliminatedTarget)
        {
            if (!Members.Contains(reporter) || eliminatedTarget != CurrentTarget) return;

            GD.Print($"Squad '{Name}' confirms target {eliminatedTarget.Name} is eliminated. Disengaging...");
            Disengage();
        }

        private void OnRepositionRequested(AIEntity member)
        {
            if (!Members.Contains(member)) return;
            (CurrentState as IRepositionHandler)?.HandleRepositionRequest(member);
        }

        private void OnMemberDestroyed(AIEntity member)
        {
            // Делегируем логику удаления общему методу, чтобы избежать дублирования
            HandleMemberRemoval(member, "destroyed");
        }

        private void OnMemberNodeAdded(Node node)
        {
            if (node is AIEntity newMember && !Members.Contains(newMember))
            {
                Members.Add(newMember);
                newMember.AssignToSquad(this);
                GD.Print($"Squad '{Name}' dynamically added member: {newMember.Name}.");

                // Если отряд уже в бою, новоприбывшему нужно отдать приказ
                if (IsInCombat && CurrentTarget != null)
                {
                    newMember.ReceiveOrderAttackTarget(CurrentTarget);
                    // Можно также отдать приказ на движение, но для простоты пока ограничимся целью
                }
            }
        }

        private void OnMemberNodeRemoved(Node node)
        {
            if (node is AIEntity member)
            {
                HandleMemberRemoval(member, "removed from tree");
            }
        }

        private void HandleMemberRemoval(AIEntity member, string reason)
        {
            // Проверяем, действительно ли этот боец еще числится в отряде.
            // Это защищает от двойного вызова (например, OnMemberDestroyed и OnMemberNodeRemoved)
            if (!Members.Contains(member)) return;

            GD.Print($"Squad '{Name}' lost member {member.Name} (Reason: {reason}).");

            Members.Remove(member);
            MembersAtDestination.Remove(member);

            if (Members.Count == 0)
            {
                GD.Print($"Squad '{Name}' has been eliminated.");
                QueueFree(); // Отряд уничтожен
                return;
            }

            // Если были в бою, нужно переоценить тактику с учетом потерь
            if (IsInCombat && IsInstanceValid(CurrentTarget))
            {
                GD.Print($"Squad '{Name}' is re-evaluating combat tactics due to member loss.");
                AssignCombatTarget(CurrentTarget);
            }
        }

        #endregion

        public void Disengage()
        {
            CurrentTarget = null;
            foreach (var member in Members) member.ClearOrders();

            if (Task is SquadTask.PatrolPath or SquadTask.AssaultPath)
            {
                ChangeState(new PatrolState(this));
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
            CurrentTarget = null;

            if (MarchingFormation == null || MarchingFormation.MemberPositions.Length == 0)
            {
                GD.PushWarning($"Squad '{Name}' has no MarchingFormation. Moving without formation.");
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