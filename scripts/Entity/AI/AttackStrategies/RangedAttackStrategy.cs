using Godot;
using Game.Projectiles;
using Game.Singletons;

namespace Game.Entity.AI.AttackStrategies;

/// <summary>
/// Стратегия атаки, основанная на выпуске снарядов.
/// </summary>
public partial class RangedAttackStrategy : Node, IAttackAction
{
    [ExportGroup("Ranged Attack Settings")]
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    private PackedScene _projectileScene;

    [Export]
    private Marker3D _muzzlePoint;

    public override void _Ready()
    {
        base._Ready();
        if (_projectileScene == null)
        {
            GD.PushError($"Для {GetPath()} не назначена сцена снаряда (_projectileScene)!");
        }
        if (_muzzlePoint == null)
        {
            GD.PushWarning($"Для {GetPath()} не назначена точка вылета снаряда (_muzzlePoint). Снаряды будут появляться в центре родителя.");
        }
    }

    public void Execute(AIEntity attacker, PhysicsBody3D target)
    {
        if (_projectileScene == null) return;

        // ДОБАВЛЕНА ПРОВЕРКА ЛИНИИ ВИДИМОСТИ ПЕРЕД ВЫСТРЕЛОМ
        if (!attacker.HasLineOfSightTo(target))
        {
            GD.Print($"{attacker.Name} cannot shoot at [{target.Name}]. No line of sight.");
            return;
        }

        var spawnTransform = _muzzlePoint?.GlobalTransform ?? attacker.GlobalTransform;
        spawnTransform = spawnTransform.LookingAt(target.GlobalPosition, Vector3.Up);

        var projectile = ProjectilePool.Get(_projectileScene);
        projectile.IgnoredEntities.Add(attacker);
        projectile.GlobalTransform = spawnTransform;

        Constants.Root.AddChild(projectile);

        // Передаем атакующего как источник урона
        projectile.Initialize(attacker);

        GD.Print($"{attacker.Name} fired at [{target.Name}].");
    }
}