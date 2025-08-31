using Game.Entity.AI.Components;
using Game.Entity.AI.Orchestrator;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entity.AI.States.Squad
{
    public class CombatState : SquadStateBase, IRepositionHandler
    {
        private readonly HashSet<AIEntity> _membersRequestingReposition = [];
        private double _repositionCheckTimer;
        private double _targetVelocityTimer;
        private double _orientationTimer;

        // Поля для хранения состояния боя
        private int _failedRepositionAttempts = 0;
        private Vector3 _lastKnownTargetPosition;
        private Vector3 _observedTargetVelocity;
        private Vector3 _targetPreviousPosition;

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

            _lastKnownTargetPosition = Squad.CurrentTarget.GlobalPosition;
            _targetPreviousPosition = Squad.CurrentTarget.GlobalPosition;

            _repositionCheckTimer = Squad.RepositionCheckInterval;
            _targetVelocityTimer = Squad.TargetVelocityTrackInterval;
            _orientationTimer = Squad.OrientationUpdateInterval;

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

            if (_observedTargetVelocity.LengthSquared() > Squad.TargetMovementThreshold * Squad.TargetMovementThreshold)
            {
                Squad.ChangeState(new PursuitState(Squad, Squad.CurrentTarget));
                return;
            }

            if (_isUsingFormationTactic)
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
                _lastKnownTargetPosition = Squad.CurrentTarget.GlobalPosition;
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
                // Немедленно запрашиваем пересчет, не дожидаясь таймера
                LegionBrain.Instance.RequestTacticalEvaluation(this);
                _repositionCheckTimer = Squad.RepositionCheckInterval; // Сбрасываем таймер
            }
        }

        private void UpdateTargetVelocity()
        {
            if (!GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;
            var displacement = Squad.CurrentTarget.GlobalPosition - _targetPreviousPosition;
            // Делим на константу, а не на изменяемый таймер
            _observedTargetVelocity = displacement / Squad.TargetVelocityTrackInterval;
            _targetPreviousPosition = Squad.CurrentTarget.GlobalPosition;
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
                _squadAnchorPoint = assignments.Values.Aggregate(Vector3.Zero, (a, b) => a + b) / assignments.Count;
                foreach (var (ai, worldPos) in assignments)
                {
                    _formationLocalOffsets[ai] = worldPos - _squadAnchorPoint;
                }
                _orientationTimer = Squad.OrientationUpdateInterval;
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
            if (!GodotObject.IsInstanceValid(Squad.CurrentTarget) || !_isUsingFormationTactic || _formationLocalOffsets.Count == 0)
            {
                _orientationTimer = Squad.OrientationUpdateInterval; // Reset or stop as appropriate
                return;
            }

            var directionToTarget = _squadAnchorPoint.DirectionTo(Squad.CurrentTarget.GlobalPosition).Normalized();
            if (directionToTarget.IsZeroApprox()) return;

            var newRotation = Basis.LookingAt(directionToTarget, Vector3.Up);

            foreach (var (ai, localOffset) in _formationLocalOffsets)
            {
                var newTargetPosition = _squadAnchorPoint + (newRotation * localOffset);
                ai.ReceiveOrderMoveTo(newTargetPosition);
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