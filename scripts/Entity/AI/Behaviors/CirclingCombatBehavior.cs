using Game.Entity.AI.States;
using Godot;

namespace Game.Entity.AI.Behaviors
{
    /// <summary>
    /// Поведение "Кружение". ИИ пытается поддерживать заданную дистанцию до цели,
    /// двигаясь боком (стрейф) и постоянно ведя огонь.
    /// </summary>
    public partial class CirclingCombatBehavior : Node, ICombatBehavior
    {
        [ExportGroup("Behavior Settings")]
        [Export] public float IdealDistance { get; private set; } = 25f;
        [Export] public float MaxDistance { get; private set; } = 35f;
        [Export(PropertyHint.Range, "0, 1, 0.1")] public float DistanceTolerance { get; private set; } = 0.2f;
        [Export] public float CirclingSpeed { get; private set; } = 8f;
        [Export] public float AttackCooldown { get; private set; } = 1.0f;

        [ExportGroup("Dependencies")]
        [Export] private Node _attackActionNode;

        private IAttackAction _attackAction;
        private double _timeSinceLastAttack = 0;
        private int _circleDirection = 1; // 1 for right, -1 for left
        private Timer _directionChangeTimer;

        public override void _Ready()
        {
            if (_attackActionNode is IAttackAction action)
            {
                _attackAction = action;
            }
            else
            {
                GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!");
            }

            _directionChangeTimer = new Timer { WaitTime = GD.RandRange(3.0, 6.0), OneShot = false };
            AddChild(_directionChangeTimer);
            _directionChangeTimer.Timeout += () =>
            {
                _circleDirection *= -1; // Меняем направление
                _directionChangeTimer.WaitTime = GD.RandRange(3.0, 6.0); // Задаем новый случайный интервал
            };
            _directionChangeTimer.Start();

            _timeSinceLastAttack = AttackCooldown;
        }

        public void Process(AIEntity context, double delta)
        {
            if (context.CurrentTarget == null || !GodotObject.IsInstanceValid(context.CurrentTarget))
            {
                context.ChangeState(new PatrolState(context));
                return;
            }

            // ИИ больше не должен поворачивать все тело к цели,
            // так как он движется боком. Вместо этого поворачиваем только "голову".
            // Старый код: context.LookAt(context.CurrentTarget.GlobalPosition, Vector3.Up);
            context.RotateHeadTowards(context.CurrentTarget.GlobalPosition, (float)delta); // <-- ИСПОЛЬЗУЕМ НОВЫЙ МЕТОД

            _timeSinceLastAttack += delta;
            Vector3 targetPos = context.CurrentTarget.GlobalPosition;
            Vector3 myPos = context.GlobalPosition;

            Vector3 directionToTarget = (targetPos - myPos).Normalized();
            float distance = myPos.DistanceTo(targetPos);

            // 1. Рассчитываем движение для поддержания дистанции
            Vector3 distanceCorrectionVelocity = Vector3.Zero;
            if (distance > IdealDistance + DistanceTolerance)
            {
                // Слишком далеко, двигаемся к цели
                distanceCorrectionVelocity = directionToTarget * context.Speed;
            }
            else if (distance < IdealDistance - DistanceTolerance)
            {
                // Слишком близко, отступаем
                distanceCorrectionVelocity = -directionToTarget * context.Speed;
            }

            // Если цель ушла слишком далеко, просто преследуем ее
            if (distance > MaxDistance)
            {
                context.MoveTo(targetPos);
                return; // Выходим, чтобы не выполнять логику стрейфа и атаки
            }

            // 2. Рассчитываем движение для стрейфа (кружения)
            Vector3 strafeDirection = directionToTarget.Cross(Vector3.Up).Normalized() * _circleDirection;
            Vector3 strafeVelocity = strafeDirection * CirclingSpeed;

            // 3. Комбинируем векторы скорости
            Vector3 targetVelocity = distanceCorrectionVelocity + strafeVelocity;
            context.Velocity = context.Velocity.Lerp(targetVelocity, context.Acceleration * (float)delta);

            // 4. Атакуем, если перезарядка прошла
            if (_timeSinceLastAttack >= AttackCooldown)
            {
                _attackAction?.Execute(context, context.CurrentTarget);
                _timeSinceLastAttack = 0;
            }
        }
    }
}