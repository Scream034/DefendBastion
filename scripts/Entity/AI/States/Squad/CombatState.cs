using Game.Entity.AI.Components;
using Game.Entity.AI.Orchestrator;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entity.AI.States.Squad
{
    public class CombatState : SquadStateBase, IRepositionHandler
    {
        private enum CombatSubState { AwaitingOrders, Repositioning, HoldingPosition }
        private CombatSubState _subState;

        private readonly HashSet<AIEntity> _membersRequestingReposition = [];
        private double _repositionCheckTimer;
        private double _targetVelocityTimer;
        private double _orientationTimer;

        // Поля для хранения состояния боя
        private int _failedRepositionAttempts = 0;

        // Поля для тактики с вращением формации
        private bool _isUsingFormationTactic = false;
        private Vector3 _squadAnchorPoint;
        private readonly Dictionary<AIEntity, Vector3> _formationLocalOffsets = [];

        /// <summary>
        /// Активен ли отряд в боевом режиме?
        /// </summary>
        public bool IsActive { get; private set; }

        public CombatState(Components.AISquad squad, LivingEntity target) : base(squad)
        {
            Squad.CurrentTarget = target;
        }

        public override void Enter()
        {
            IsActive = true;
            GD.Print($"Squad '{Squad.SquadName}' engaging target {Squad.CurrentTarget.Name}.");
            foreach (var member in Squad.Members) member.ClearOrders();

            // Инициализация таймеров
            _repositionCheckTimer = Squad.RepositionCheckInterval;
            _targetVelocityTimer = Squad.TargetVelocityTrackInterval;
            _orientationTimer = Squad.OrientationUpdateInterval;

            _subState = CombatSubState.AwaitingOrders;

            LegionBrain.Instance.RequestTacticalEvaluation(this);
        }

        public override void Process(double delta)
        {
            if (!IsActive || !GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;

            _repositionCheckTimer -= delta;
            _targetVelocityTimer -= delta;

            if (_targetVelocityTimer <= 0)
            {
                UpdateTargetVelocity();
                _targetVelocityTimer = Squad.TargetVelocityTrackInterval;
            }

            if (_repositionCheckTimer <= 0)
            {
                LegionBrain.Instance.RequestTacticalEvaluation(this);
                _repositionCheckTimer = Squad.RepositionCheckInterval;
            }

            float targetDisplacementSq = Squad.CurrentTarget.GlobalPosition.DistanceSquaredTo(Squad.LastKnownTargetPosition);
            // Если цель сместилась более чем на 2 метра, а мы еще не замерили скорость - это повод для погони
            bool hasMovedSignificantly = targetDisplacementSq > 4f;

            // Основное условие: высокая измеренная скорость ИЛИ значительное смещение
            if (Squad.ObservedTargetVelocity.LengthSquared() > Squad.TargetMovementThreshold * Squad.TargetMovementThreshold || hasMovedSignificantly)
            {
                // Если переходим по смещению, а скорость еще 0, давайте "подтолкнем" ее,
                // чтобы PursuitState не вышел сразу.
                if (Squad.ObservedTargetVelocity.IsZeroApprox() && hasMovedSignificantly)
                {
                    var direction = Squad.LastKnownTargetPosition.DirectionTo(Squad.CurrentTarget.GlobalPosition);
                    Squad.ObservedTargetVelocity = direction * Squad.TargetMovementThreshold; // Придаем "искусственную" минимальную скорость
                }

                Squad.ChangeState(new PursuitState(Squad, Squad.CurrentTarget));
                return;
            }

            // Мы проверяем переход в HoldingPosition ТОЛЬКО если находимся в Repositioning
            if (_subState == CombatSubState.Repositioning)
            {
                int membersAtDestination = 0;
                foreach (var member in Squad.Members)
                {
                    if (member.MovementController.HasReachedDestination())
                    {
                        membersAtDestination++;
                    }
                }

                if (membersAtDestination >= Squad.Members.Count)
                {
                    GD.Print($"Squad '{Squad.SquadName}' has taken positions. Switching to HOLDING mode.");
                    _subState = CombatSubState.HoldingPosition;
                    // Инициализируем таймер вращения только сейчас
                    _orientationTimer = Squad.OrientationUpdateInterval;
                }
            }

            // Вращение формации работает ТОЛЬКО в режиме удержания
            if (_isUsingFormationTactic && _subState == CombatSubState.HoldingPosition)
            {
                _orientationTimer -= delta;
                if (_orientationTimer <= 0)
                {
                    UpdateFormationOrientation();
                    _orientationTimer = Squad.OrientationUpdateInterval;
                }
            }

            bool hasLineOfSight = false;
            foreach (var member in Squad.Members)
            {
                if (member.GetVisibleTargetPoint(Squad.CurrentTarget).HasValue)
                {
                    hasLineOfSight = true;
                    break;
                }
            }
            if (hasLineOfSight)
            {
                // Обновляем поле в Squad
                Squad.LastKnownTargetPosition = Squad.CurrentTarget.GlobalPosition;
            }
        }

        public override void Exit()
        {
            IsActive = false;
        }

        public void HandleRepositionRequest(AIEntity member)
        {
            _membersRequestingReposition.Add(member);
            if (Squad.Members.Count > 1 && _membersRequestingReposition.Count >= Squad.Members.Count * 0.6f)
            {
                GD.Print($"Squad '{Squad.SquadName}' forces re-evaluation due to member requests.");
                LegionBrain.Instance.RequestTacticalEvaluation(this);
                _repositionCheckTimer = Squad.RepositionCheckInterval;
            }
        }

        private void UpdateTargetVelocity()
        {
            if (!GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;
            var displacement = Squad.CurrentTarget.GlobalPosition - Squad.TargetPreviousPosition;
            // Обновляем поля в Squad
            Squad.ObservedTargetVelocity = displacement / Squad.TargetVelocityTrackInterval;
            Squad.TargetPreviousPosition = Squad.CurrentTarget.GlobalPosition;
        }

        public void ExecuteTacticalEvaluation()
        {
            if (!GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;

            _membersRequestingReposition.Clear();
            _isUsingFormationTactic = false;
            _formationLocalOffsets.Clear();
            _orientationTimer = Squad.OrientationUpdateInterval;

            GD.Print($"Squad '{Squad.SquadName}' is re-evaluating combat positions.");

            var assignments = FindBestCombatPositions(Squad.CurrentTarget);

            if (assignments != null && assignments.Count > 0)
            {
                _failedRepositionAttempts = 0;
                AssignOrdersFromDictionary(assignments);

                // Если была выбрана формация, запоминаем ее структуру для вращения
                GD.Print("Activating formation tactic. Storing offsets for dynamic orientation.");
                _isUsingFormationTactic = true;
                // ВАЖНО: _squadAnchorPoint теперь вычисляется один раз и остается статичным на время передислокации
                Vector3 anchorPointSum = Vector3.Zero;
                foreach (var pos in assignments.Values)
                {
                    anchorPointSum += pos;
                }
                _squadAnchorPoint = anchorPointSum / assignments.Count;

                foreach (var (ai, worldPos) in assignments)
                {
                    _formationLocalOffsets[ai] = worldPos - _squadAnchorPoint;
                }
            }
            else
            {
                _failedRepositionAttempts++;
                GD.PushWarning($"Failed to find static positions, attempt #{_failedRepositionAttempts}.");
                if (_failedRepositionAttempts >= 2)
                {
                    GD.PushError("Too many failed attempts. Forcing pursuit tactic.");
                    Squad.ChangeState(new PursuitState(Squad, Squad.CurrentTarget));
                }
                else
                {
                    ExecuteDirectAssaultTactic();
                }
            }
        }

        private void UpdateFormationOrientation()
        {
            // Этот метод теперь вызывается только когда все стоят на местах.
            // Его задача - лишь слегка "довернуть" отряд, если цель немного сдвинулась.

            // Якорь можно оставить статичным, как центр идеальных позиций.
            // А можно обновлять до центра масс, т.к. теперь это не вызовет "юления".
            // Давайте попробуем обновлять, это сделает поведение более живым.
            _squadAnchorPoint = Squad.GetSquadCenter();

            var directionToTarget = _squadAnchorPoint.DirectionTo(Squad.CurrentTarget.GlobalPosition).Normalized();
            if (directionToTarget.IsZeroApprox()) return;

            var newRotation = Basis.LookingAt(directionToTarget, Vector3.Up);

            foreach (var (ai, localOffset) in _formationLocalOffsets)
            {
                var newTargetPosition = _squadAnchorPoint + (newRotation * localOffset);

                // Здесь можно оставить старую проверку, она не помешает.
                const float repositionThresholdSq = 2f;
                var currentPos = ai.GlobalPosition; // Так как AI не движется, его текущая позиция и есть его "цель"

                if (currentPos.DistanceSquaredTo(newTargetPosition) > repositionThresholdSq)
                {
                    ai.ReceiveOrderMoveTo(newTargetPosition);
                    _subState = CombatSubState.Repositioning;
                }
            }
        }

        #region Tactical Execution
        private Dictionary<AIEntity, Vector3> FindBestCombatPositions(LivingEntity target)
        {
            var representative = Squad.Members.FirstOrDefault(m => GodotObject.IsInstanceValid(m) && m.CombatBehavior?.Action?.MuzzlePoint != null);
            Vector3 muzzleOffset = representative?.CombatBehavior.Action.MuzzlePoint.Position ?? Vector3.Zero;
            var assignments = AITacticalAnalysis.FindCoverAndFirePositions(Squad.Members, target, muzzleOffset);
            if (assignments == null || assignments.Count < Squad.Members.Count)
            {
                assignments = AITacticalAnalysis.GeneratePositionsFromFormation(Squad.Members, Squad.CombatFormation, target, muzzleOffset);
            }
            if (assignments == null || assignments.Count < Squad.Members.Count)
            {
                var arcPositions = AITacticalAnalysis.GenerateFiringArcPositions(Squad.Members, target);
                if (arcPositions != null && arcPositions.Count > 0)
                {
                    assignments = AITacticalAnalysis.GetOptimalAssignments(Squad.Members, arcPositions, target.GlobalPosition);
                }
            }
            return assignments;
        }

        private void AssignOrdersFromDictionary(Dictionary<AIEntity, Vector3> assignments)
        {
            foreach (var (ai, position) in assignments)
            {
                ai.ReceiveOrderMoveTo(position);
                ai.ReceiveOrderAttackTarget(Squad.CurrentTarget);
            }

            foreach (var member in Squad.Members.Where(m => !assignments.ContainsKey(m)))
            {
                GD.PushWarning($"Member {member.Name} did not receive a position, ordering direct assault.");
                member.ReceiveOrderMoveTo(Squad.CurrentTarget.GlobalPosition);
                member.ReceiveOrderAttackTarget(Squad.CurrentTarget);
            }
        }

        private void ExecuteDirectAssaultTactic()
        {
            GD.PushWarning("Executing direct assault.");
            foreach (var member in Squad.Members)
            {
                member.ReceiveOrderMoveTo(Squad.CurrentTarget.GlobalPosition);
                member.ReceiveOrderAttackTarget(Squad.CurrentTarget);
            }
        }
        #endregion
    }
}