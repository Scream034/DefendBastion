using Godot;
using Game.Entity.AI.Behaviors;
using Game.Entity.AI.States;
using System.Threading.Tasks;
using Game.Entity.AI.Profiles;
using Game.Entity.AI.Components;
using Game.Singletons;

namespace Game.Entity.AI
{
    public enum AIMainTask { FreePatrol, PathPatrol, Assault }
    public enum AssaultMode { Destroy, Rush }

    public abstract partial class AIEntity : MoveableEntity
    {
        [ExportGroup("AI Configuration")]
        [Export] public AIProfile Profile { get; private set; }
        [Export] public AIMainTask MainTask { get; private set; } = AIMainTask.FreePatrol;
        [Export] public AssaultMode AssaultBehavior { get; private set; } = AssaultMode.Destroy;
        [Export] public Path3D MissionPath { get; private set; }

        [ExportGroup("Dependencies")]
        [Export] private AITargetingSystem _targetingSystem;
        [Export] private AIMovementController _movementController;
        [Export] private AILookController _lookController;
        [Export] private Node _combatBehaviorNode;
        [Export] private Marker3D _eyesPosition;

        public AITargetingSystem TargetingSystem => _targetingSystem;
        public AIMovementController MovementController => _movementController;
        public AILookController LookController => _lookController;
        public ICombatBehavior CombatBehavior { get; private set; }
        public Marker3D EyesPosition => _eyesPosition;

        public bool IsMoving => Velocity.LengthSquared() > 0.01f;
        public bool IsTargetValid => GodotObject.IsInstanceValid(TargetingSystem.CurrentTarget);
        public bool HasLineOfSightToCurrentTarget => GetVisibleTargetPoint(TargetingSystem.CurrentTarget).HasValue;
        public bool IsInCombat { get; private set; }

        public Vector3 SpawnPosition { get; private set; }
        public Vector3 LastKnownTargetPosition { get; private set; }
        public Vector3 PursuitTargetPosition { get; private set; }
        public Vector3 InvestigationPosition { get; set; }
        public Vector3 LastEngagementPosition { get; set; }

        private LivingEntity _lastTrackedTarget;

        private State _currentState;
        private State _defaultState;
        private bool _isAiActive = false;

        public override async void _Ready()
        {
            base._Ready();
            SpawnPosition = GlobalPosition;

            // Используйте #if DEBUG, чтобы избежать этих проверок в релизном билде
            if (!ValidateDependencies())
            {
                SetPhysicsProcess(false);
                GD.PushError($"Произошла ошибка инициализации AI: {Name}");
                return;
            }

            _targetingSystem.Initialize(this);
            _movementController.Initialize(this);
            _lookController.Initialize(this);

            if (World.Instance != null && World.Instance.IsNavigationReady) InitializeAI();
            else
            {
                GD.Print($"{Name} waiting for navigation map...");
                await ToSignal(World.Instance, World.SignalName.NavigationReady);
                InitializeAI();
            }
        }

        private void InitializeAI()
        {
            if (_isAiActive) return;
            GD.Print($"{Name} initializing AI, navigation map is ready.");
            _isAiActive = true;
            _defaultState = MainTask switch
            {
                AIMainTask.PathPatrol or AIMainTask.Assault => new PathFollowingState(this),
                _ => new PatrolState(this),
            };
            ChangeState(_defaultState);
        }

        private bool ValidateDependencies()
        {
            if (Profile == null) { GD.PushError($"AIProfile not assigned to {Name}!"); return false; }
            if (_targetingSystem == null) { GD.PushError($"AITargetingSystem not assigned to {Name}!"); return false; }
            if (_movementController == null) { GD.PushError($"AIMovementController not assigned to {Name}!"); return false; }
            if (_lookController == null) { GD.PushError($"AILookController not assigned to {Name}!"); return false; }
            if (_combatBehaviorNode is ICombatBehavior behavior) { CombatBehavior = behavior; }
            else { GD.PushError($"CombatBehavior for {Name} is not assigned or invalid!"); return false; }
            if (_eyesPosition == null) { GD.PushWarning($"EyesPosition not assigned to {Name}. Line-of-sight checks will originate from the entity's center."); }
            if ((MainTask == AIMainTask.PathPatrol || MainTask == AIMainTask.Assault) && MissionPath == null)
            {
                GD.PushError($"AI {Name} is set to PathPatrol or Assault but has no MissionPath assigned!");
                return false;
            }
            return true;
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            if (!_isAiActive) return;

            if (IsTargetValid)
            {
                _lastTrackedTarget = TargetingSystem.CurrentTarget;
                LastKnownTargetPosition = TargetingSystem.CurrentTarget.GlobalPosition;
            }

            _currentState?.Update((float)delta);
        }

        public override async Task<bool> DamageAsync(float amount, LivingEntity source = null)
        {
            if (!await base.DamageAsync(amount, source)) return false;
            if (source != null && _currentState is not AttackState && TargetingSystem.CurrentTarget == null)
            {
                InvestigationPosition = source.GlobalPosition;
                ChangeState(new InvestigateState(this));
            }
            return true;
        }

        #region Line-of-Sight Methods

        public Vector3? GetVisibleTargetPoint(LivingEntity target)
        {
            var fromPosition = EyesPosition?.GlobalPosition ?? GlobalPosition;
            return GetVisibleTargetPointFrom(target, fromPosition);
        }

        public Vector3? GetVisibleTargetPointFrom(LivingEntity target, Vector3 fromPosition)
        {
            if (!IsInstanceValid(target)) return null;

            uint mask = Profile?.CombatProfile?.LineOfSightMask ?? 1;
            var exclude = new Godot.Collections.Array<Rid> { GetRid(), target.GetRid() };
            var resultPoint = AITacticalAnalysis.GetFirstVisiblePoint(fromPosition, target, exclude, mask);

            return resultPoint;
        }

        #endregion

        #region State Machine and Event Handling
        public void ChangeState(State newState)
        {
            if (_currentState?.GetType() == newState.GetType()) return; // Не меняем состояние на такое же

            _currentState?.Exit();
            _currentState = newState;

            // Устанавливаем флаг IsInCombat в зависимости от нового состояния
            IsInCombat = newState is AttackState;

            GD.Print($"{Name} changing state to -> {newState.GetType().Name}");
            _currentState.Enter();
        }

        public void ReturnToDefaultState()
        {
            TargetingSystem.ClearTarget();
            ChangeState(_defaultState);
        }

        public void SetPursuitTargetPosition(Vector3 position)
        {
            PursuitTargetPosition = position;
        }

        public void OnNewTargetAcquired(LivingEntity newTarget)
        {
            if (_currentState is not AttackState && _currentState is not PursuitState)
            {
                ChangeState(new AttackState(this));
            }
        }

        public void OnCurrentTargetInvalidated()
        {
            var lastKnownPos = LastKnownTargetPosition;
            // Цель могла быть уничтожена. _lastTrackedTarget может быть невалидным, поэтому мы его не передаем.
            TargetingSystem.OnTargetEliminated(_lastTrackedTarget);
            _lastTrackedTarget = null;

            // Решаем, что делать дальше.
            DecideNextActionAfterCombat(lastKnownPos);
        }

        /// <summary>
        /// Вызывается, когда AI достигает конца своего `MissionPath` в режиме `Assault`.
        /// </summary>
        public void OnMissionPathCompleted()
        {
            if (MainTask == AIMainTask.Assault)
            {
                GD.Print($"{Name} has reached the assault objective. Switching to free combat/patrol.");
                // После достижения цели штурма, AI переходит в режим свободного патруля/боя.
                _defaultState = new PatrolState(this);
                ReturnToDefaultState();
            }
        }

        private void DecideNextActionAfterCombat(Vector3 lastEngagementPosition)
        {
            // Не переходим в Vigilance, если мы должны продолжать штурм
            if (Profile.CombatProfile.EnablePostCombatVigilance && (MainTask != AIMainTask.Assault || AssaultBehavior != AssaultMode.Rush))
            {
                LastEngagementPosition = lastEngagementPosition;
                ChangeState(new VigilanceState(this));
            }
            else if (!IsTargetValid)
            {
                ReturnToDefaultState();
            }
        }
        #endregion

        #region API for States
        public void SetMovementSpeed(float speed)
        {
            Speed = speed;
        }
        #endregion
    }
}