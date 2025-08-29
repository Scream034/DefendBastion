using Godot;
using Game.Entity.AI.Behaviors;
using Game.Entity.AI.States;
using System.Threading.Tasks;
using Game.Interfaces;
using System.Collections.Generic;
using System.Linq;

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

        [ExportGroup("AI Targetting System")]
        /// <summary>
        /// Как часто (в секундах) ИИ будет переоценивать цели в своем списке.
        /// Более низкие значения делают ИИ более реактивным, но увеличивают нагрузку.
        /// </summary>
        [Export(PropertyHint.Range, "0.1, 2.0, 0.1")]
        public float TargetEvaluationInterval = 0.5f;

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

        /// <summary>
        /// Список всех враждебных сущностей, которые в данный момент находятся в зоне обнаружения ИИ.
        /// </summary>
        private readonly List<PhysicsBody3D> _potentialTargets = [];

        private float _targetEvaluationTimer;

        protected AIEntity(IDs id) : base(id) { }
        public AIEntity() { }

        public override async void _Ready()
        {
            base._Ready();
            SpawnPosition = GlobalPosition;
            _targetEvaluationTimer = TargetEvaluationInterval; // Первый запуск оценки произойдет через interval

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

            // Периодически запускаем оценку целей
            _targetEvaluationTimer -= (float)delta;
            if (_targetEvaluationTimer <= 0f)
            {
                EvaluateTargets();
                _targetEvaluationTimer = TargetEvaluationInterval;
            }

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
                Basis = Basis.Orthonormalized().Slerp(targetRotation, BodyRotationSpeed * (float)delta);
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
            if (target == this) return;

            // Если новая цель отличается от текущей, или у нас не было цели
            if (target != CurrentTarget)
            {
                // Проверяем, что цель вообще можно атаковать
                if (target is not ICharacter || target is not IFactionMember)
                {
                    GD.PrintErr($"Attempted to target {target.Name}, which is not a valid target.");
                    return;
                }

                CurrentTarget = target;
                GD.Print($"{Name} new best target is: {target.Name}");

                // Если мы не в состоянии атаки/преследования/расследования, переходим в атаку
                if (_currentState is not (AttackState or PursuitState or InvestigateState))
                {
                    ChangeState(new AttackState(this));
                }
            }
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

        private void EvaluateTargets()
        {
            // 1. Очищаем список от уничтоженных или невалидных целей
            _potentialTargets.RemoveAll(t => !IsInstanceValid(t) || (t is ICharacter d && d.Health <= 0));

            if (_potentialTargets.Count == 0)
            {
                // Если в зоне видимости не осталось врагов, и мы были в бою,
                // то нужно перейти в состояние преследования последней цели.
                // Этот переход обрабатывается в AttackState при потере LoS.
                return;
            }

            // 2. Получаем лучшую цель от нашего оценщика
            var bestTarget = AITargetEvaluator.GetBestTarget(this, _potentialTargets);

            // 3. Устанавливаем лучшую цель как текущую
            if (bestTarget != null)
            {
                SetAttackTarget(bestTarget);
            }
            // Если bestTarget == null (например, все цели за стеной), мы сохраняем текущую цель
            // и продолжим ее преследовать, пока не потеряем.
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
                var targetRotation = Basis.LookingAt(direction.Normalized()).Orthonormalized();
                Basis = Basis.Orthonormalized().Slerp(targetRotation, BodyRotationSpeed * delta);
            }
        }

        public void RotateHeadTowards(Vector3 targetPoint, float delta)
        {
            if (_headPivot == null) return;
            var localTarget = _headPivot.ToLocal(targetPoint).Normalized();
            var targetRotation = Basis.LookingAt(localTarget).Orthonormalized();
            _headPivot.Basis = _headPivot.Basis.Orthonormalized().Slerp(targetRotation, HeadRotationSpeed * delta);
        }
        #endregion

        #region Signal Handlers
        private void OnTargetDetected(Node3D body)
        {
            GD.Print($"{Name} Dected {body.Name}");
            if (body is PhysicsBody3D physicsBody && body is IFactionMember factionMember && body != this)
            {
                GD.Print($"{Name} finding {body.Name} ({factionMember.Faction}).");
                if (IsHostile(factionMember) && body is ICharacter)
                {
                    if (!_potentialTargets.Contains(physicsBody))
                    {
                        GD.Print($"{Name} added {body.Name} to potential targets.");
                        _potentialTargets.Add(physicsBody);
                        // Немедленно запускаем оценку, чтобы быстрее среагировать на нового врага
                        EvaluateTargets();
                    }
                }
            }
        }

        private void OnTargetLost(Node3D body)
        {
            if (body is PhysicsBody3D physicsBody)
            {
                if (_potentialTargets.Remove(physicsBody))
                {
                    GD.Print($"{Name} removed {body.Name} from potential targets.");
                }

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
        }
        #endregion
    }
}
