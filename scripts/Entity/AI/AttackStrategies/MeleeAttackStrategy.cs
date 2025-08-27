using Godot;

namespace Game.Entity.AI.AttackStrategies
{
    public partial class MeleeAttackStrategy : Node, IAttackAction
    {
        [ExportGroup("Melee Attack Settings")]
        [Export(PropertyHint.Range, "1,500,1")]
        private float _damage = 25f;

        public void Execute(AIEntity attacker, LivingEntity target)
        {
            GD.Print($"[{attacker.Name}] melees [{target.Name}] for {_damage} damage.");
            // Передаем себя (атакующего) как источник урона
            _ = target.DamageAsync(_damage, attacker);
        }
    }
}