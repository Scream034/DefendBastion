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
    public Marker3D MuzzlePoint { get; private set; }

    public override void _Ready()
    {
        base._Ready();
        if (_projectileScene == null)
        {
            GD.PushError($"Для {GetPath()} не назначена сцена снаряда (_projectileScene)!");
        }
        if (MuzzlePoint == null)
        {
            GD.PushWarning($"Для {GetPath()} не назначена точка вылета снаряда (MuzzlePoint). Снаряды будут появляться в центре родителя.");
        }
    }

    public void Execute(AIEntity attacker, PhysicsBody3D target)
    {
        if (_projectileScene == null) return;

        var spawnTransform = MuzzlePoint?.GlobalTransform ?? attacker.GlobalTransform;
        spawnTransform = spawnTransform.LookingAt(target.GlobalPosition, Vector3.Up);

        var projectile = ProjectilePool.Get(_projectileScene);
        projectile.RayQueryParams.Exclude.Add(attacker.GetRid());
        projectile.GlobalTransform = spawnTransform;

        Constants.Root.AddChild(projectile);

        projectile.Initialize(attacker);

        GD.Print($"{attacker.Name} fired at [{target.Name}].");
    }
}