using Game.Entity.AI.States;
using Godot;

namespace Game.Entity.AI.Behaviors
{
    /// <summary>
    /// Поведение "Стой и стреляй". ИИ преследует цель, останавливается на дистанции атаки и атакует.
    /// </summary>
    public partial class StationaryCombatBehavior : Node, ICombatBehavior
    {
        [ExportGroup("Behavior Settings")]
        [Export] public float AttackRange { get; private set; } = 15f;
        [Export] public float AttackCooldown { get; private set; } = 2.0f;

        [ExportGroup("Dependencies")]
        [Export] private Node _attackActionNode;

        private IAttackAction _attackAction;
        private double _timeSinceLastAttack = 0;

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
            // Устанавливаем в значение кулдауна, чтобы первая атака была мгновенной
            _timeSinceLastAttack = AttackCooldown;
        }

        public void Process(AIEntity context, double delta)
        {
            if (context.CurrentTarget == null || !GodotObject.IsInstanceValid(context.CurrentTarget))
            {
                context.ChangeState(new PatrolState(context));
                return;
            }

            _timeSinceLastAttack += delta;
            var targetPosition = context.CurrentTarget.GlobalPosition;
            float distanceToTarget = context.GlobalPosition.DistanceTo(targetPosition);

            if (distanceToTarget > AttackRange)
            {
                context.MoveTo(targetPosition);
                // Вращение тела в сторону движения будет обработано автоматически в AIEntity._PhysicsProcess
            }
            else
            {
                context.StopMovement();
                // Вместо резкого LookAt используем новый плавный метод
                context.RotateBodyTowards(targetPosition, (float)delta);

                if (_timeSinceLastAttack >= AttackCooldown)
                {
                    _attackAction?.Execute(context, context.CurrentTarget);
                    _timeSinceLastAttack = 0;
                }
            }
        }
    }
}