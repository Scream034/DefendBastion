using Godot;
using Game.Entity.AI.Behaviors;
using Game.Entity.AI.States;
using System.Threading.Tasks;
using Game.Interfaces;

namespace Game.Entity.AI
{
    /// <summary>
    /// Главная задача, которую выполняет ИИ, когда не находится в бою.
    /// </summary>
    public enum AIMainTask
    {
        /// <summary>Патрулирование в случайных точках в радиусе от спавна.</summary>
        FreePatrol,
        /// <summary>Патрулирование по заданному пути (Path3D).</summary>
        PathPatrol,
        /// <summary>Движение по пути к конечной цели.</summary>
        Assault
    }

    /// <summary>
    /// Режим поведения при выполнении задачи "Штурм".
    /// </summary>
    public enum AssaultMode
    {
        /// <summary>Атаковать всех врагов на пути к цели.</summary>
        Destroy,
        /// <summary>Игнорировать врагов и как можно быстрее добраться до цели.</summary>
        Rush
    }

    /// <summary>
    /// Базовый класс для всех сущностей, управляемых ИИ.
    /// Инкапсулирует логику навигации, машину состояний и базовое поведение.
    /// </summary>
    public abstract partial class AIEntity : MoveableEntity
    {
        [ExportGroup("AI Mission Settings")]
        [Export] public AIMainTask MainTask { get; private set; } = AIMainTask.FreePatrol;
        [Export] public AssaultMode AssaultBehavior { get; private set; } = AssaultMode.Destroy;
        [Export] public Path3D MissionPath { get; private set; }

        [ExportGroup("AI Movement Profiles")]
        [Export] public float SlowSpeed { get; private set; } = 3.0f;
        [Export] public float NormalSpeed { get; private set; } = 5.0f;
        [Export] public float FastSpeed { get; private set; } = 8.0f;


        [ExportGroup("AI Patrol Parameters")]
        [Export(PropertyHint.Range, "1, 20, 0.5")] public float BodyRotationSpeed { get; private set; } = 10f;
        [Export(PropertyHint.Range, "1, 20, 0.5")] public float HeadRotationSpeed { get; private set; } = 15f;
        [Export] public bool UseRandomPatrolRadius { get; private set; } = true;
        [Export] public float MinPatrolRadius { get; private set; } = 5f;
        [Export] public float MaxPatrolRadius { get; private set; } = 20f;
        [Export] public bool UseRandomWaitTime { get; private set; } = true;
        [Export(PropertyHint.Range, "0, 10, 0.5")] public float MinPatrolWaitTime { get; private set; } = 1.0f;
        [Export(PropertyHint.Range, "0, 10, 0.5")] public float MaxPatrolWaitTime { get; private set; } = 3.0f;

        [ExportGroup("Dependencies")]
        [Export] private NavigationAgent3D _navigationAgent;
        [Export] private Area3D _targetDetectionArea;
        [Export] private Node _combatBehaviorNode;
        [Export] private Node3D _headPivot;
        [Export] private Marker3D _eyesPosition; // Точка для проверки линии видимости

        public NavigationAgent3D NavigationAgent => _navigationAgent;
        
        public PhysicsBody3D CurrentTarget { get; private set; }
        public ICombatBehavior CombatBehavior { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Vector3 LastKnownTargetPosition { get; set; }
        public Vector3 InvestigationPosition { get; set; }

        private State _currentState;
        private State _defaultState; // Состояние, к которому ИИ возвращается после боя/тревоги
        private bool _isAiActive = false;

        protected AIEntity(IDs id) : base(id) { }
        public AIEntity() { }

        public override async void _Ready()
        {
            base._Ready();
            SpawnPosition = GlobalPosition;

            if (!ValidateDependencies())
            {
                SetPhysicsProcess(false);
                return;
            }

            if (_targetDetectionArea != null)
            {
                _targetDetectionArea.BodyEntered += OnTargetDetected;
                _targetDetectionArea.BodyExited += OnTargetLost;
            }

            if (GameManager.Instance.IsNavigationReady)
            {
                InitializeAI();
            }
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

            // GlobalPosition = NavigationServer3D.MapGetClosestPoint(GetWorld3D().NavigationMap, GlobalPosition);

            GD.Print($"{Name} initializing AI, navigation map is ready.");
            _isAiActive = true;

            // Определяем "домашнее" состояние на основе настроек
            _defaultState = MainTask switch
            {
                AIMainTask.PathPatrol or AIMainTask.Assault => new PathFollowingState(this),
                _ => new PatrolState(this),
            };
            ChangeState(_defaultState);
        }

        private bool ValidateDependencies()
        {
            if (_navigationAgent == null) { GD.PushError($"NavigationAgent3D not assigned to {Name}!"); return false; }
            if (_combatBehaviorNode is ICombatBehavior behavior) { CombatBehavior = behavior; }
            else { GD.PushError($"CombatBehavior for {Name} is not assigned or invalid!"); return false; }

            if (_headPivot == null) { GD.PushWarning($"HeadPivot not assigned to {Name}. Head rotation will not work."); }
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

            _currentState?.Update((float)delta);

            Vector3 targetVelocity = Vector3.Zero;

            if (_isAiActive && !_navigationAgent.IsNavigationFinished())
            {
                var nextPoint = _navigationAgent.GetNextPathPosition();
                var direction = GlobalPosition.DirectionTo(nextPoint);
                targetVelocity = direction * Speed;
            }

            Velocity = Velocity.Lerp(targetVelocity, Acceleration * (float)delta);

            var horizontalVelocity = Velocity with { Y = 0 };
            if (horizontalVelocity.LengthSquared() > 0.1f)
            {
                var targetRotation = Basis.LookingAt(horizontalVelocity.Normalized()).Orthonormalized();
                Basis = Basis.Slerp(targetRotation, BodyRotationSpeed * (float)delta);
            }

            MoveAndSlide();
        }

        public override async Task<bool> DamageAsync(float amount, LivingEntity source = null)
        {
            if (!await base.DamageAsync(amount, source)) return false;

            // РЕАКЦИЯ НА УРОН
            if (source != null && _currentState is not AttackState)
            {
                GD.Print($"{Name} took damage from {source.Name} direction!");
                InvestigationPosition = source.GlobalPosition;
                // Немедленно переходим в состояние расследования, если мы не в активном бою с другой целью
                if (CurrentTarget == null)
                {
                    ChangeState(new InvestigateState(this));
                }
            }
            return true;
        }

        public void ChangeState(State newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            GD.Print($"{Name} changing state to -> {newState.GetType().Name}");
            _currentState.Enter();
        }

        public void ReturnToDefaultState()
        {
            ClearTarget();
            ChangeState(_defaultState);
        }

        public void SetAttackTarget(PhysicsBody3D target)
        {
            if (target == this || target == null) return;
            
            // Убедимся, что цель вообще можно атаковать и у нее есть фракция
            if (target is not IDamageable || target is not IFactionMember)
            {
                GD.PrintErr($"Attempted to target {target.Name}, which is not a valid target (IDamageable & IFactionMember).");
                return;
            }

            CurrentTarget = target;
            ChangeState(new AttackState(this));
        }

        public void ClearTarget()
        {
            CurrentTarget = null;
        }

        public bool HasLineOfSightTo(PhysicsBody3D target)
        {
            if (target == null) return false;

            var spaceState = GetWorld3D().DirectSpaceState;
            var query = new PhysicsRayQueryParameters3D
            {
                // Начальная точка - "глаза" ИИ, или его центр, если они не заданы
                From = _eyesPosition?.GlobalPosition ?? GlobalPosition,
                // Конечная точка - центр цели
                To = target.GlobalPosition,
                // Исключаем себя и цель из столкновения луча (чтобы луч не уперся в нас самих)
                Exclude = [GetRid(), target.GetRid()],
                // Проверяем столкновение с геометрией мира (стены и т.д.)
                CollisionMask = 1 // Предполагаем, что мир находится на 1-м слое физики
            };

            var result = spaceState.IntersectRay(query);

            // Если result пустой, значит, на пути луча ничего не было - есть прямая видимость
            return result.Count == 0;
        }

        #region Movement API for States
        public void SetMovementSpeed(float speed)
        {
            Speed = speed;
        }

        public void MoveTo(Vector3 targetPosition)
        {
            if (_navigationAgent.TargetPosition == targetPosition) return;
            _navigationAgent.TargetPosition = targetPosition;
        }

        public void StopMovement()
        {
            _navigationAgent.TargetPosition = GlobalPosition;
        }
        #endregion

        #region Rotation API
        public void RotateBodyTowards(Vector3 targetPoint, float delta)
        {
            var direction = GlobalPosition.DirectionTo(targetPoint) with { Y = 0 };
            if (!direction.IsZeroApprox())
            {
                var targetRotation = Basis.LookingAt(direction.Normalized());
                Basis = Basis.Slerp(targetRotation, BodyRotationSpeed * delta);
            }
        }

        public void RotateHeadTowards(Vector3 targetPoint, float delta)
        {
            if (_headPivot == null) return;
            var localTarget = _headPivot.ToLocal(targetPoint).Normalized();
            var targetRotation = Basis.LookingAt(localTarget);
            _headPivot.Basis = _headPivot.Basis.Slerp(targetRotation, HeadRotationSpeed * delta);
        }
        #endregion

        #region Signal Handlers
        private void OnTargetDetected(Node3D body)
        {
#if DEBUG
            GD.Print($"{Name} detected {body.Name}!");
#endif
            // Атакуем только если нет текущей цели, это подходящая сущность и она нам враждебна
            if (CurrentTarget == null && body is IFactionMember factionMember && body != this)
            {
                if (IsHostile(factionMember) && body is IDamageable)
                {
                    SetAttackTarget((PhysicsBody3D)body);
                }
            }
        }

        private void OnTargetLost(Node3D body)
        {
            // Если мы потеряли из виду *текущую* цель
            if (body == CurrentTarget)
            {
                // Не очищаем цель сразу, а переходим в преследование
                if (_currentState is AttackState)
                {
                    LastKnownTargetPosition = CurrentTarget.GlobalPosition;
                    ChangeState(new PursuitState(this));
                }
            }
        }
        #endregion
    }
}
