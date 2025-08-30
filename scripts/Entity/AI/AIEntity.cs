using Godot;
using Game.Entity.AI.Behaviors;
using Game.Entity.AI.States;
using System.Threading.Tasks;
using Game.Interfaces;
using Game.Entity.AI.Profiles;
using Game.Entity.AI.Components;

namespace Game.Entity.AI
{
    public enum AIMainTask { FreePatrol, PathPatrol, Assault }
    public enum AssaultMode { Destroy, Rush }

    public abstract partial class AIEntity : MoveableEntity
    {
        [ExportGroup("AI Configuration")]
        [Export] public AIProfile Profile { get; private set; }

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

        public Vector3 SpawnPosition { get; private set; }
        public Vector3 LastKnownTargetPosition { get; private set; }
        public Vector3 PursuitTargetPosition { get; private set; }
        public Vector3 InvestigationPosition { get; set; }
        public Vector3 LastEngagementPosition { get; set; }

        private LivingEntity _lastTrackedTarget;

        private Vector3? _cachedVisiblePoint = null;
        private bool _isLoSCacheValidThisFrame = false;
        private ulong _cacheFrame = ulong.MaxValue;
        private Vector3 _cachedLoS_SelfPosition;
        private Vector3 _cachedLoS_TargetPosition;
        private const float LoSCacheInvalidationDistanceSqr = 0.0625f; // 0.25 * 0.25

        private State _currentState;
        private State _defaultState;
        private bool _isAiActive = false;

        public override async void _Ready()
        {
            base._Ready();
            SpawnPosition = GlobalPosition;

#if DEBUG
            if (!ValidateDependencies())
            {
                SetPhysicsProcess(false);
                throw new System.Exception($"Произошла ошибка инициализации AI: {Name}");
            }
#endif
            _targetingSystem.Initialize(this);
            _movementController.Initialize(this);
            _lookController.Initialize(this);
            Profile.Initialize(this);

            if (GameManager.Instance.IsNavigationReady) InitializeAI();
            else
            {
                GD.Print($"{Name} waiting for navigation map...");
                await ToSignal(GameManager.Instance, GameManager.SignalName.NavigationReady);
                InitializeAI();
            }
        }

        private void InitializeAI()
        {
            if (_isAiActive) return;
            GD.Print($"{Name} initializing AI, navigation map is ready.");
            _isAiActive = true;
            _defaultState = Profile.MainTask switch
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
            if ((Profile.MainTask == AIMainTask.PathPatrol || Profile.MainTask == AIMainTask.Assault) && Profile.MissionNodePath == null)
            {
                GD.PushError($"AI {Name} is set to PathPatrol or Assault but has no MissionPath assigned in its profile!");
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
                // Эта позиция теперь обновляется всегда, когда есть валидная цель.
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

            var currentFrame = (ulong)GetTree().GetFrame();
            if (_cacheFrame == currentFrame &&
                _cachedLoS_SelfPosition.DistanceSquaredTo(GlobalPosition) < LoSCacheInvalidationDistanceSqr &&
                _cachedLoS_TargetPosition.DistanceSquaredTo(target.GlobalPosition) < LoSCacheInvalidationDistanceSqr)
            {
                return _cachedVisiblePoint;
            }

            var exclude = new Godot.Collections.Array<Rid> { GetRid(), target.GetRid() };
            var resultPoint = AITacticalAnalysis.GetFirstVisiblePoint(this, fromPosition, target, exclude);

            _cachedVisiblePoint = resultPoint;
            _cacheFrame = currentFrame;
            _cachedLoS_SelfPosition = GlobalPosition;
            _cachedLoS_TargetPosition = target.GlobalPosition;

            return resultPoint;
        }

        #endregion

        #region State Machine and Event Handling
        public void ChangeState(State newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            GD.Print($"{Name} changing state to -> {newState.GetType().Name}");
            _currentState.Enter();
        }

        public void ReturnToDefaultState()
        {
            TargetingSystem.ClearTarget();
            ChangeState(_defaultState);
        }

        /// <summary>
        /// Проверяет, является ли текущая цель всё ещё наилучшей.
        /// Возвращает true, если цель - пустая турель, И есть другие потенциальные цели.
        /// </summary>
        // public bool NeedsToReevaluateTarget()
        // {
        //     if (!IsTargetValid) return false;

        //     var currentTarget = TargetingSystem.CurrentTarget;

        //     // Проверяем, является ли цель пустой управляемой турелью.
        //     if (currentTarget is ControllableTurret { CurrentController: null })
        //     {
        //         // Если это так, нужно переоценивать, ТОЛЬКО ЕСЛИ есть другие варианты.
        //         // Если в списке потенциальных целей больше одной записи (сама турель + кто-то еще),
        //         // или если есть цели, но текущая - не единственная, значит, есть смысл искать лучшую.
        //         // Самый простой способ: есть ли в списке хоть кто-то, кроме текущей цели.
        //         return TargetingSystem.PotentialTargets.Any(t => t != currentTarget);
        //     }

        //     return false;
        // }

        /// Устанавливает конкретную точку, к которой должно двигаться состояние преследования.
        /// </summary>
        public void SetPursuitTargetPosition(Vector3 position)
        {
            PursuitTargetPosition = position;
        }

        public void OnNewTargetAcquired(PhysicsBody3D newTarget)
        {
            if (_currentState is not AttackState && _currentState is not PursuitState)
            {
                ChangeState(new AttackState(this));
            }
        }

        public void OnTargetLostLineOfSight(PhysicsBody3D lostTarget)
        {
            if (lostTarget == TargetingSystem.CurrentTarget && _currentState is AttackState)
            {
                ChangeState(new PursuitState(this));
            }
        }

        public void OnCurrentTargetInvalidated()
        {
            if (!GodotObject.IsInstanceValid(_lastTrackedTarget))
            {
                // Используем LastKnownTargetPosition, так как это была последняя валидная позиция _lastTrackedTarget
                OnTargetEliminated(_lastTrackedTarget, LastKnownTargetPosition);
            }
            else
            {
                ReturnToDefaultState();
            }
        }

        /// <summary>
        /// Централизованный обработчик события уничтожения цели.
        /// Содержит новую логику для расследования уничтоженных контейнеров.
        /// </summary>
        private void OnTargetEliminated(LivingEntity eliminatedEntity, Vector3 lastKnownPosition)
        {
            TargetingSystem.OnTargetEliminated(eliminatedEntity);
            _lastTrackedTarget = null;

            // Проверяем, не уничтожили ли мы контейнер, и следует ли нам проверить, не остался ли кто-то внутри.
            if (eliminatedEntity is IContainerEntity && ShouldInvestigateAfterContainerDestroy())
            {
                // Если да, то вместо стандартного поведения после боя, мы начинаем расследование.
                StartInvestigation(lastKnownPosition);
            }
            else
            {
                // Иначе используем стандартную логику (бдительность или возврат к задачам).
                DecideNextActionAfterCombat(lastKnownPosition);
            }
        }

        /// <summary>
        /// Запускает состояние расследования в указанной точке.
        /// </summary>
        private void StartInvestigation(Vector3 position)
        {
            GD.Print($"{Name} destroyed a container. Investigating the area for occupants at {position}.");
            InvestigationPosition = position;
            ChangeState(new InvestigateState(this));
        }

        /// <summary>
        /// Определяет, должен ли AI расследовать место уничтожения контейнера на основе своей текущей задачи.
        /// </summary>
        private bool ShouldInvestigateAfterContainerDestroy()
        {
            // В режиме "Прорыв" мы игнорируем все отвлекающие факторы и бежим к цели.
            if (Profile.MainTask == AIMainTask.Assault && Profile.AssaultBehavior == AssaultMode.Rush)
            {
                return false;
            }
            // Во всех остальных режимах (Патруль, Штурм/Уничтожение) расследование является
            // правильным тактическим решением.
            return true;
        }

        public void DecideNextActionAfterCombat(Vector3 lastEngagementPosition)
        {
            if (Profile.CombatProfile.EnablePostCombatVigilance && (Profile.MainTask != AIMainTask.Assault || Profile.AssaultBehavior != AssaultMode.Rush))
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