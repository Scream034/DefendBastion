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

            // --- БЛОК 1: НАБЛЮДЕНИЕ И ДОКЛАД (без приказа) ---
            if (!_hasAttackOrder)
            {
                var visibleTarget = TargetingSystem.PotentialTargets
                    .FirstOrDefault(t => IsInstanceValid(t) && GetVisibleTargetPoint(t).HasValue);

                if (visibleTarget != null)
                {
                    LegionBrain.Instance.ReportEnemySighting(this, visibleTarget);
                }
                return;
            }

            // --- БЛОК 2: ВЫПОЛНЕНИЕ ПРИКАЗОВ (есть приказ атаковать) ---
            if (!IsInstanceValid(_attackTarget) || !_attackTarget.IsAlive)
            {
                ClearOrders();
                return;
            }

            bool isAtDestination = true; // Считаем, что мы на месте, если нет приказа двигаться

            // --- Логика движения ---
            if (_hasMoveOrder)
            {
                if (GlobalPosition.DistanceSquaredTo(_moveTarget) < 1.0f) // Погрешность в 1 метр
                {
                    // Мы прибыли
                    _hasMoveOrder = false;
                    MovementController.StopMovement();
                    Squad?.ReportPositionReached(this);
                    isAtDestination = true;
                }
                else
                {
                    // Мы еще в пути
                    isAtDestination = false;
                }
            }

            // <--- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ ЛОГИКИ АТАКИ --->
            // Если мы получили приказ на атаку И мы уже на своей позиции (или нам и не приказывали двигаться),
            // мы можем начинать стрелять.
            if (_hasAttackOrder && isAtDestination)
            {
                var fromPos = CombatBehavior?.Action?.MuzzlePoint?.GlobalPosition ?? GlobalPosition;
                if (AITacticalAnalysis.HasClearPath(fromPos, _attackTarget.GlobalPosition, [GetRid(), _attackTarget.GetRid()], Profile.CombatProfile.LineOfSightMask))
                {
                    // Цель в зоне видимости, ведем огонь
                    if (_timeSinceLastAttack >= CombatBehavior.AttackCooldown)
                    {
                        CombatBehavior?.Action?.Execute(this, _attackTarget, _attackTarget.GlobalPosition);
                        _timeSinceLastAttack = 0;
                    }
                }
                else
                {
                    // Мы на позиции, но цель скрылась. Докладываем, чтобы отряд перестроился.
                    ClearOrders();
                    LegionBrain.Instance.ReportEnemySighting(this, _attackTarget);
                }
            }
        }

        public override async Task<bool> DestroyAsync()
        {
            Squad?.OnMemberDestroyed(this);
            return await base.DestroyAsync();
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

        public Vector3? GetVisibleTargetPoint(LivingEntity target)
        {
            var fromPosition = EyesPosition?.GlobalPosition ?? GlobalPosition;
            if (!IsInstanceValid(target)) return null;

            uint mask = Profile?.CombatProfile?.LineOfSightMask ?? 1;
            var exclude = new Godot.Collections.Array<Rid> { GetRid(), target.GetRid() };
            return AITacticalAnalysis.GetFirstVisiblePoint(fromPosition, target, exclude, mask);
        }

        #endregion
    }
}