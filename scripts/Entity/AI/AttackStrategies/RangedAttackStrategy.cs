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

    public void Execute(AIEntity attacker, LivingEntity target)
    {
        if (_projectileScene == null) return;

        // Определяем точку спавна. Если muzzlePoint не задан, используем позицию самого атакующего.
        var spawnTransform = _muzzlePoint?.GlobalTransform ?? attacker.GlobalTransform;

        // Поворачиваем точку спавна в сторону цели, чтобы снаряд полетел правильно.
        spawnTransform = spawnTransform.LookingAt(target.GlobalPosition, Vector3.Up);

        var projectile = ProjectilePool.Get(_projectileScene);
        projectile.IgnoredEntities.Add(attacker);
        projectile.GlobalTransform = spawnTransform;

        // Добавляем снаряд в корень сцены, чтобы его жизненный цикл не зависел от ИИ.
        Constants.Root.AddChild(projectile);

        // Инициализируем, указывая, кто стрелял, чтобы снаряд не попал в самого себя.
        projectile.Initialize(attacker);

        GD.Print($"[{attacker.Name}] выполнил выстрел в [{target.Name}] с помощью {Name}.");
    }
}