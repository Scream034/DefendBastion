using Godot;

namespace Game.Entity.AI.AttackStrategies;

/// <summary>
/// Стратегия атаки для ближнего боя. Наносит прямой урон цели.
/// </summary>
public partial class MeleeAttackStrategy : Node, IAttackAction
{
    [ExportGroup("Melee Attack Settings")]
    [Export(PropertyHint.Range, "1,500,1")]
    private float _damage = 25f;

    // TODO: Можно добавить ссылку на AnimationPlayer для проигрывания анимации удара
    // [Export] private AnimationPlayer _animationPlayer;

    public void Execute(AIEntity attacker, LivingEntity target)
    {
        GD.Print($"[{attacker.Name}] наносит удар ближнего боя по [{target.Name}] на {_damage} урона.");

        // _animationPlayer?.Play("Attack");

        _ = target.DamageAsync(_damage);
    }
}