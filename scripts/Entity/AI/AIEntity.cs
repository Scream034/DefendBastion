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

        // --- Свойства для внутреннего и внешнего использования ---
        public AISquad Squad { get; private set; }
        public ICombatBehavior CombatBehavior { get; private set; }
        public bool IsInCombat => _hasAttackOrder; // Простой способ определить, в бою ли AI
        public bool IsMoving => Velocity.LengthSquared() > 0.01f;

        // --- Текущие приказы от отряда ---
        private Vector3 _moveTarget;
        private LivingEntity _attackTarget;
        private bool _hasMoveOrder;
        private bool _hasAttackOrder;

        // --- Внутреннее состояние ---
        private double _timeSinceLastAttack;

        /// <summary>
        /// Если нет LoS x секунд, просим сменить позицию
        /// </summary>
        private const float REPOSITION_REQUEST_THRESHOLD = 2f;
        /// <summary>
        /// Время без LoS в секундах
        /// </summary>
        private double _timeWithoutLoS = 0;
        private bool _hasRequestedReposition = false;

        public override async void _Ready()
        {
            base._Ready();

            if (!ValidateDependencies())
            {
                SetPhysicsProcess(false);
                GD.PushError($"Произошла ошибка инициализации AI: {Name}");
                return;
            }

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

        private void InitializeAI()
        {
            SetMovementSpeed(Profile.MovementProfile.NormalSpeed);
        }

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

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            _timeSinceLastAttack += delta;

            if (!_hasAttackOrder)
            {
                ProcessOutOfCombatLogic();
                return;
            }

            if (!IsInstanceValid(_attackTarget) || !_attackTarget.IsAlive)
            {
                Squad?.ReportTargetEliminated(this, _attackTarget);
                ClearOrders();
                return;
            }

            // --- НОВАЯ УМНАЯ ЛОГИКА С ДВУМЯ ДИСТАНЦИЯМИ ---

            // 1. Рассчитываем обе дистанции (в квадратах для производительности)
            float maxAttackRange = CombatBehavior.AttackRange;
            float engagementRange = maxAttackRange * Profile.CombatProfile.EngagementRangeFactor;

            float maxAttackRangeSq = maxAttackRange * maxAttackRange;
            float engagementRangeSq = engagementRange * engagementRange;

            float distanceToTargetSq = GlobalPosition.DistanceSquaredTo(_attackTarget.GlobalPosition);

            // 2. Определяем наше тактическое состояние
            bool isBeyondMaxRange = distanceToTargetSq > maxAttackRangeSq;
            bool isInOptimalRange = distanceToTargetSq <= engagementRangeSq;
            // "Серая зона": мы можем стрелять, но хотим подойти ближе
            bool isInFiringEnvelope = !isBeyondMaxRange && !isInOptimalRange;

            Vector3? aimPoint = GetMuzzleLineOfFirePoint(_attackTarget); // Проверяем LoS в любом случае

            // --- ЛОГИКА ПРИНЯТИЯ РЕШЕНИЙ ---

            // СЦЕНАРИЙ 1: Мы слишком далеко. Только движение.
            if (isBeyondMaxRange)
            {
                // Продолжаем считать время без LoS, так как мы не можем атаковать
                // Движение к цели управляется отрядом (через _hasMoveOrder).
                // Если приказа двигаться нет, AI будет ждать, пока командир не даст новый приказ.
                // Наша система переоценки должна это обработать.
                _timeWithoutLoS += delta;
                return; // Стрелять не можем, выходим из боевой логики.
            }

            // СЦЕНАРИЙ 2: Мы в оптимальной зоне для атаки. Приоритет - огонь.
            if (isInOptimalRange)
            {
                if (aimPoint.HasValue) // Если видим цель
                {
                    // Позиция идеальна. Останавливаемся и ведем огонь.
                    if (_hasMoveOrder || MovementController.NavigationAgent.Velocity.LengthSquared() > 0.1f)
                    {
                        MovementController.StopMovement();
                        _hasMoveOrder = false;
                        Squad?.ReportPositionReached(this);
                    }

                    // Атакуем, если можем
                    FireIfReady(aimPoint.Value);
                }
                else // Не видим цель, даже находясь близко
                {
                    RequestRepositionIfNeeded(delta);
                }
                return;
            }

            // СЦЕНАРИЙ 3: Мы в "серой зоне" (можем стрелять, но хотим подойти ближе).
            if (isInFiringEnvelope)
            {
                // Здесь самая интересная логика: "преследование с огнем".
                // Мы НЕ останавливаемся. Движение продолжается, если есть приказ.

                if (aimPoint.HasValue) // Если видим цель
                {
                    // Стреляем "на ходу", не прекращая движения к цели.
                    FireIfReady(aimPoint.Value);
                }
                else // Не видим цель, хотя должны бы
                {
                    RequestRepositionIfNeeded(delta);
                }
                // Движение к _moveTarget продолжается, управляемое AIMovementController
                return;
            }
        }

        public override async Task<bool> DestroyAsync()
        {
            Squad?.OnMemberDestroyed(this);
            return await base.DestroyAsync();
        }

        /// <summary>
        /// Логика, выполняемая, когда у AI нет приказа на атаку.
        /// В основном, это поиск и доклад о целях.
        /// </summary>
        private void ProcessOutOfCombatLogic()
        {
            // Если отряд уже в бою, отдельным бойцам не нужно искать новые цели.
            if (Squad != null && Squad.CurrentState == SquadState.InCombat)
            {
                return;
            }

            // Используем "глаза" для обнаружения.
            var visibleTarget = TargetingSystem.PotentialTargets
                .FirstOrDefault(t => IsInstanceValid(t) && GetVisibleTargetPoint(t).HasValue);

            if (visibleTarget != null)
            {
                // Докладываем "мозгу", чтобы отряд получил приказ.
                LegionBrain.Instance.ReportEnemySighting(this, visibleTarget);
            }
        }

        /// <summary>
        /// Проверяет линию огня от дула оружия до цели.
        /// </summary>
        /// <returns>Точка прицеливания, если линия огня чиста, иначе null.</returns>
        private Vector3? GetMuzzleLineOfFirePoint(LivingEntity target)
        {
            var fromPosition = CombatBehavior?.Action?.MuzzlePoint?.GlobalPosition ?? GlobalPosition;

            // Исключаем из проверки ТОЛЬКО себя. Цель (target) больше не исключается.
            // Это позволяет новой логике в AITacticalAnalysis правильно определить,
            // попал ли луч в цель или в препятствие перед ней.
            var exclude = new Godot.Collections.Array<Rid> { GetRid() };

            uint mask = Profile.CombatProfile.LineOfSightMask;

            return AITacticalAnalysis.GetFirstVisiblePointOfTarget(fromPosition, target, exclude, mask);
        }

        #region Orders API

        public void AssignToSquad(AISquad squad) => Squad = squad;

        public void ReceiveOrderMoveTo(Vector3 position)
        {
            _moveTarget = position;
            _hasMoveOrder = true;
            MovementController.MoveTo(position);
            LookController.SetInterestPoint(position);

            // Когда нам дают новый приказ на движение, это значит,
            // что наш старый запрос на перестроение был учтен. Сбрасываем флаг.
            _hasRequestedReposition = false;
            _timeWithoutLoS = 0;
        }

        public void ReceiveOrderAttackTarget(LivingEntity target)
        {
            _attackTarget = target;
            _hasAttackOrder = true;
            TargetingSystem.ForceSetCurrentTarget(target);
            // LookController автоматически подхватит цель из TargetingSystem,
            // поэтому SetInterestPoint здесь не обязателен, но для явности можно оставить.
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

        #region Other

        public void SetMovementSpeed(float speed)
        {
            Speed = speed;
        }

        /// <summary>
        /// Проверяет видимость цели из "глаз" для первоначального обнаружения.
        /// </summary>
        public Vector3? GetVisibleTargetPoint(LivingEntity target)
        {
            var fromPosition = EyesPosition?.GlobalPosition ?? GlobalPosition;
            if (!IsInstanceValid(target)) return null;

            uint mask = Profile?.CombatProfile?.LineOfSightMask ?? 1;

            // Аналогичное исправление и для этой функции.
            var exclude = new Godot.Collections.Array<Rid> { GetRid() };
            return AITacticalAnalysis.GetFirstVisiblePointOfTarget(fromPosition, target, exclude, mask);
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
            if (_hasAttackOrder && !_hasMoveOrder) // Мы стоим на месте в бою, но не можем стрелять
            {
                _timeWithoutLoS += delta;
                if (_timeWithoutLoS > REPOSITION_REQUEST_THRESHOLD && !_hasRequestedReposition)
                {
                    GD.Print($"{Name} cannot see target for {_timeWithoutLoS:F1}s. Requesting reposition.");
                    Squad?.RequestReposition(this);
                    _hasRequestedReposition = true;
                }
            }
        }

        #endregion
    }
}