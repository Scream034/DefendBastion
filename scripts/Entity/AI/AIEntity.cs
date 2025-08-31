using Godot;
using Game.Entity.AI.Behaviors;
using Game.Entity.AI.Profiles;
using Game.Entity.AI.Components;
using Game.Entity.AI.Orchestrator;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Entity.AI
{
    public abstract partial class AIEntity : MoveableEntity
    {
        [ExportGroup("AI Configuration")]
        [Export] public AIProfile Profile { get; private set; }

        [ExportGroup("Dependencies")]
        [Export] public AITargetingSystem TargetingSystem { get; private set; }
        [Export] public AIMovementController MovementController { get; private set; }
        [Export] public AILookController LookController { get; private set; }
        [Export] public Marker3D EyesPosition { get; private set; }
        [Export] private Node _combatBehaviorNode;

        public AISquad Squad { get; private set; }
        public ICombatBehavior CombatBehavior { get; private set; }
        public bool IsInCombat => _hasAttackOrder;
        public bool IsMoving => Velocity.LengthSquared() > 0.01f;

        // Текущие приказы
        private LivingEntity _attackTarget;
        private bool _hasMoveOrder;
        private bool _hasAttackOrder;

        // Внутреннее состояние
        private double _timeSinceLastAttack;
        private const float REPOSITION_REQUEST_THRESHOLD = 2f;
        private double _timeWithoutLoS = 0;
        private bool _hasRequestedReposition = false;

        private float _maxAttackRangeSq;
        private float _engagementRangeSq;

        public override async void _Ready()
        {
            base._Ready();
            if (!ValidateDependencies())
            {
                SetPhysicsProcess(false);
                GD.PushError($"Произошла ошибка инициализации AI: {Name}");
                return;
            }

            float maxAttackRange = CombatBehavior.AttackRange;
            _maxAttackRangeSq = maxAttackRange * maxAttackRange;
            _engagementRangeSq = maxAttackRange * Profile.CombatProfile.EngagementRangeFactor *
                                 (maxAttackRange * Profile.CombatProfile.EngagementRangeFactor);

            TargetingSystem.Initialize(this);
            MovementController.Initialize(this);
            LookController.Initialize(this);

            if (World.Instance != null && World.Instance.IsNavigationReady) InitializeAI();
            else
            {
                await ToSignal(World.Instance, World.SignalName.NavigationReady);
                InitializeAI();
            }

        }

        private void InitializeAI() => SetMovementSpeed(Profile.MovementProfile.NormalSpeed);

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            _timeSinceLastAttack += delta;

            if (_hasAttackOrder)
            {
                ProcessCombatLogic(delta);
            }
            else
            {
                ProcessOutOfCombatLogic();
            }
        }

        private void ProcessCombatLogic(double delta)
        {
            if (!IsInstanceValid(_attackTarget) || !_attackTarget.IsAlive)
            {
                AISignals.Instance.EmitSignal(AISignals.SignalName.TargetEliminated, this, _attackTarget);
                ClearOrders();
                return;
            }

            float distanceToTargetSq = GlobalPosition.DistanceSquaredTo(_attackTarget.GlobalPosition);
            bool isBeyondMaxRange = distanceToTargetSq > _maxAttackRangeSq;
            bool isInOptimalRange = distanceToTargetSq <= _engagementRangeSq;
            bool isInFiringEnvelope = !isBeyondMaxRange && !isInOptimalRange;
            Vector3? aimPoint = GetMuzzleLineOfFirePoint(_attackTarget);

            if (isBeyondMaxRange)
            {
                _timeWithoutLoS += delta; // Мы не можем атаковать, считаем время без LoS
                return;
            }

            if (isInOptimalRange)
            {
                if (aimPoint.HasValue)
                {
                    if (_hasMoveOrder) // Позиция идеальна, останавливаемся
                    {
                        MovementController.StopMovement();
                        _hasMoveOrder = false;
                        Squad?.ReportPositionReached(this); // Это единственный оставшийся прямой вызов, он оправдан для внутренней логики отряда
                    }
                    FireIfReady(aimPoint.Value);
                }
                else
                {
                    RequestRepositionIfNeeded(delta);
                }
                return;
            }

            if (isInFiringEnvelope)
            {
                if (aimPoint.HasValue)
                {
                    FireIfReady(aimPoint.Value); // Стреляем на ходу
                }
                else
                {
                    RequestRepositionIfNeeded(delta);
                }
            }
        }

        private void ProcessOutOfCombatLogic()
        {
            if (Squad != null && Squad.IsInCombat) return;

            LivingEntity visibleTarget = null;
            foreach (var target in TargetingSystem.PotentialTargets)
            {
                if (IsInstanceValid(target) && GetVisibleTargetPoint(target).HasValue)
                {
                    visibleTarget = target;
                    break; // Нашли первую цель, выходим из цикла
                }
            }

            if (visibleTarget != null)
            {
                AISignals.Instance.EmitSignal(AISignals.SignalName.EnemySighted, this, visibleTarget);
            }
        }

        private void FireIfReady(Vector3 aimPoint)
        {
            _timeWithoutLoS = 0;
            _hasRequestedReposition = false;
            if (_timeSinceLastAttack >= CombatBehavior.AttackCooldown)
            {
                CombatBehavior.Action?.Execute(this, _attackTarget, aimPoint);
                _timeSinceLastAttack = 0;
            }
        }

        private void RequestRepositionIfNeeded(double delta)
        {
            if (_hasAttackOrder && !_hasMoveOrder)
            {
                _timeWithoutLoS += delta;
                if (_timeWithoutLoS > REPOSITION_REQUEST_THRESHOLD && !_hasRequestedReposition)
                {
                    _hasRequestedReposition = true;
                    // ЭМИТИРУЕМ СОБЫТИЕ вместо прямого вызова
                    AISignals.Instance.EmitSignal(AISignals.SignalName.RepositionRequested, this);
                    GD.Print($"{Name} cannot see target. Requesting reposition.");
                }
            }
        }

        public override async Task<bool> DestroyAsync()
        {
            // ЭМИТИРУЕМ СОБЫТИЕ
            AISignals.Instance.EmitSignal(AISignals.SignalName.MemberDestroyed, this);
            return await base.DestroyAsync();
        }

        #region Orders API (Не изменилось)
        public void AssignToSquad(AISquad squad) => Squad = squad;

        public void ReceiveOrderMoveTo(Vector3 position)
        {
            _hasMoveOrder = true;
            MovementController.MoveTo(position);
            LookController.SetInterestPoint(position);
            _hasRequestedReposition = false;
            _timeWithoutLoS = 0;
        }

        public void ReceiveOrderAttackTarget(LivingEntity target)
        {
            _attackTarget = target;
            _hasAttackOrder = true;
            TargetingSystem.ForceSetCurrentTarget(target);
            LookController.SetInterestPoint(target.GlobalPosition);
        }

        public void ClearOrders()
        {
            _hasAttackOrder = false;
            _hasMoveOrder = false;
            _attackTarget = null;
            TargetingSystem.ForceSetCurrentTarget(null);
            MovementController.StopMovement();
            LookController.SetInterestPoint(null);
        }
        #endregion

        #region Validation and Utils (Без существенных изменений)
        // ... (Код ValidateDependencies, GetMuzzleLineOfFirePoint, GetVisibleTargetPoint, etc. остается здесь)
        private bool ValidateDependencies()
        {
            if (Profile == null) { GD.PushError($"AIProfile not assigned to {Name}!"); return false; }
            if (TargetingSystem == null) { GD.PushError($"AITargetingSystem not assigned to {Name}!"); return false; }
            if (MovementController == null) { GD.PushError($"AIMovementController not assigned to {Name}!"); return false; }
            if (LookController == null) { GD.PushError($"AILookController not assigned to {Name}!"); return false; }
            if (_combatBehaviorNode is ICombatBehavior behavior) { CombatBehavior = behavior; }
            else { GD.PushError($"CombatBehavior for {Name} is not assigned or invalid!"); return false; }
            if (EyesPosition == null) { GD.PushWarning($"EyesPosition not assigned to {Name}. Line-of-sight checks will originate from the entity's center."); }
            return true;
        }

        public void SetMovementSpeed(float speed) => Speed = speed;

        public Vector3? GetVisibleTargetPoint(LivingEntity target)
        {
            var fromPosition = EyesPosition?.GlobalPosition ?? GlobalPosition;
            if (!IsInstanceValid(target)) return null;
            uint mask = Profile?.CombatProfile?.LineOfSightMask ?? 1;
            var exclude = new Godot.Collections.Array<Rid> { GetRid() };
            return AITacticalAnalysis.GetFirstVisiblePointOfTarget(fromPosition, target, exclude, mask);
        }

        private Vector3? GetMuzzleLineOfFirePoint(LivingEntity target)
        {
            var fromPosition = CombatBehavior?.Action?.MuzzlePoint?.GlobalPosition ?? GlobalPosition;
            var exclude = new Godot.Collections.Array<Rid> { GetRid() };
            uint mask = Profile.CombatProfile.LineOfSightMask;
            return AITacticalAnalysis.GetFirstVisiblePointOfTarget(fromPosition, target, exclude, mask);
        }
        #endregion
    }
}