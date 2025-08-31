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

            // --- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Проверяем и линию огня, и дальность атаки ---

            // 1. Проверяем дальность
            float attackRangeSq = CombatBehavior.AttackRange * CombatBehavior.AttackRange;
            bool isInRange = GlobalPosition.DistanceSquaredTo(_attackTarget.GlobalPosition) <= attackRangeSq;

            // 2. Проверяем линию огня (только если мы в принципе в радиусе атаки)
            Vector3? aimPoint = null;
            if (isInRange)
            {
                aimPoint = GetMuzzleLineOfFirePoint(_attackTarget);
            }

            // Теперь главный вопрос: есть ли у нас чистая линия огня И находимся ли мы на нужной дистанции?
            if (isInRange && aimPoint.HasValue)
            {
                // --- У НАС ЕСТЬ ЛИНИЯ ОГНЯ И МЫ В РАДИУСЕ ПОРАЖЕНИЯ ---
                // 1. Прекратить движение.
                if (_hasMoveOrder || MovementController.NavigationAgent.Velocity.LengthSquared() > 0.1f)
                {
                    MovementController.StopMovement();
                    _hasMoveOrder = false;
                    Squad?.ReportPositionReached(this);
                }

                // 2. Атаковать, если готова перезарядка.
                if (_timeSinceLastAttack >= CombatBehavior.AttackCooldown)
                {
                    CombatBehavior.Action?.Execute(this, _attackTarget, aimPoint.Value);
                    _timeSinceLastAttack = 0;
                }
            }
            else
            {
                // --- У НАС НЕТ ЛИНИИ ОГНЯ И/ИЛИ МЫ СЛИШКОМ ДАЛЕКО ---
                // 1. Мы должны двигаться к назначенной тактической позиции.
                if (_hasMoveOrder)
                {
                    // Если мы уже на "плохой" позиции (нет LoS или далеко), сообщаем отряду.
                    // Отряд должен будет принять решение о передислокации.
                    if (MovementController.NavigationAgent.IsNavigationFinished())
                    {
                        _hasMoveOrder = false;
                        MovementController.StopMovement();
                        Squad?.ReportPositionReached(this); // Сообщаем, что мы на позиции (хоть она и плохая)
                    }
                    // Если еще не дошли - MovementController продолжает работу.
                }
                // Если приказа двигаться нет, но мы внезапно оказались вне зоны досягаемости 
                // (например, цель отбежала), AI будет просто ждать новых приказов от отряда.
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

        #region API для командной системы

        public void AssignToSquad(AISquad squad) => Squad = squad;

        public void ReceiveOrderMoveTo(Vector3 position)
        {
            _moveTarget = position;
            _hasMoveOrder = true;
            MovementController.MoveTo(position);
            LookController.SetInterestPoint(position);
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

        #region Вспомогательные методы

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

        #endregion
    }
}