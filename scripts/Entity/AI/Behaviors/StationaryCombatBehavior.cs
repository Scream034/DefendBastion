using Godot;

namespace Game.Entity.AI.Behaviors
{
    public partial class StationaryCombatBehavior : Node, ICombatBehavior
    {
        [Export] public float AttackRange { get; private set; } = 15f;
        [Export] public float AttackCooldown { get; private set; } = 2.0f;
        [Export] private Node _attackActionNode;

        public IAttackAction Action { get; private set; }

        private double _timeSinceLastAttack = 0;
        private bool _isRepositioning = false;

        public override void _Ready()
        {
            if (_attackActionNode is IAttackAction action) Action = action;
            else { GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!"); SetProcess(false); return; }

            AttackCooldown *= (float)GD.RandRange(1f, 1.5f);

            _timeSinceLastAttack = AttackCooldown;
        }

        public void EnterCombat(AIEntity context)
        {
            _timeSinceLastAttack = AttackCooldown;
        }

        public void ExitCombat(AIEntity context)
        {

        }
    }
}