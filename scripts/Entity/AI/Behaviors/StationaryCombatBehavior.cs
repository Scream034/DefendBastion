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
        private double _effectiveAttackCooldown;
        private bool _isRepositioning = false;

        public override void _Ready()
        {
            if (_attackActionNode is IAttackAction action) Action = action;
            else { GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!"); SetProcess(false); return; }

            var variance = (float)GD.RandRange(-0.1, 0.1);
            _effectiveAttackCooldown = AttackCooldown * (1.0f + variance);

            _timeSinceLastAttack = _effectiveAttackCooldown; 
        }

        public void EnterCombat(AIEntity context)
        {
            _timeSinceLastAttack = _effectiveAttackCooldown;
        }

        public void ExitCombat(AIEntity context)
        {

        }
    }
}