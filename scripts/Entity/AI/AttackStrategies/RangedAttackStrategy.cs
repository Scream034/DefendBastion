using Godot;
using Game.Projectiles;
using System.Reflection.Metadata;
using Game.Singletons;

namespace Game.Entity.AI.AttackStrategies;

/// <summary>
/// Стратегия атаки, основанная на выпуске снарядов.
/// Поддерживает настраиваемое упреждение цели и разброс.
/// </summary>
public partial class RangedAttackStrategy : Node, IAttackAction
{
    [ExportGroup("Ranged Attack Settings")]
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    private PackedScene _projectileScene;

    [Export]
    public Marker3D MuzzlePoint { get; private set; }

    private float _projectileSpeedCache = -1f;

    public override void _Ready()
    {
        base._Ready();
        if (_projectileScene == null)
        {
            GD.PushError($"Для {GetPath()} не назначена сцена снаряда (_projectileScene)!");
            return;
        }
        if (MuzzlePoint == null)
        {
            GD.PushWarning($"Для {GetPath()} не назначена точка вылета снаряда (MuzzlePoint). Снаряды будут появляться в центре родителя.");
        }

        // Кэшируем скорость снаряда для производительности.
        // Это избегает инстанцирования сцены при каждом выстреле.
        var projectileInstance = _projectileScene.InstantiateOrNull<BaseProjectile>();
        if (projectileInstance != null)
        {
            _projectileSpeedCache = projectileInstance.Speed;
            projectileInstance.QueueFree();
        }
        else
        {
            GD.PushError($"Сцена '{_projectileScene.ResourcePath}' в {GetPath()} не содержит узел, наследуемый от BaseProjectile.");
        }
    }

    public void Execute(AIEntity attacker, LivingEntity target, Vector3 aimPosition)
    {
        if (_projectileScene == null || _projectileSpeedCache <= 0) return;

        var combatProfile = attacker.Profile?.CombatProfile;
        if (combatProfile == null)
        {
            GD.PushWarning($"AICombatProfile не найден для {attacker.Name}. Стрельба будет идеальной.");
        }

        Vector3 finalAimPosition = aimPosition;

        // 1. Логика упреждения цели
        if (combatProfile?.EnableTargetPrediction == true && target is MoveableEntity moveableTarget)
        {
            var predictedPosition = PredictAimPosition(attacker, moveableTarget, _projectileSpeedCache, combatProfile.PredictionAccuracy);
            if (predictedPosition.HasValue)
            {
                finalAimPosition = predictedPosition.Value;
            }
        }

        // 2. Логика разброса
        if (combatProfile?.AimSpreadRadius > 0)
        {
            finalAimPosition = ApplyAimSpread(finalAimPosition, combatProfile.AimSpreadRadius);
        }

        var spawnTransform = MuzzlePoint?.GlobalTransform ?? attacker.GlobalTransform;
        spawnTransform = spawnTransform.LookingAt(finalAimPosition, Vector3.Up);

        var projectile = ProjectilePool.Get(_projectileScene);
        projectile.RayQueryParams?.Exclude.Add(attacker.GetRid());
        projectile.GlobalTransform = spawnTransform;

        Constants.Root.AddChild(projectile); // Используем централизованный контейнер

        projectile.Initialize(attacker);

        GD.Print($"{attacker.Name} fired at [{target.Name}] aiming for [{finalAimPosition}].");
    }

    /// <summary>
    /// Рассчитывает точку упреждения для движущейся цели.
    /// </summary>
    /// <returns>Рассчитанная точка прицеливания или null, если расчет невозможен.</returns>
    private Vector3? PredictAimPosition(AIEntity attacker, MoveableEntity target, float projectileSpeed, float accuracy)
    {
        Vector3 attackerPos = MuzzlePoint?.GlobalPosition ?? attacker.GlobalPosition;
        Vector3 targetPos = target.GlobalPosition;
        Vector3 targetVel = target.Velocity;

        Vector3 deltaPos = targetPos - attackerPos;

        // Решаем квадратное уравнение a*t^2 + b*t + c = 0 для времени столкновения t
        float a = targetVel.Dot(targetVel) - projectileSpeed * projectileSpeed;
        float b = 2 * deltaPos.Dot(targetVel);
        float c = deltaPos.Dot(deltaPos);

        float discriminant = b * b - 4 * a * c;

        if (discriminant < 0)
        {
            return null; // Нет реального решения, цель движется слишком быстро
        }

        // Находим наименьшее положительное время t
        float t1 = (-b + Mathf.Sqrt(discriminant)) / (2 * a);
        float t2 = (-b - Mathf.Sqrt(discriminant)) / (2 * a);
        float timeToHit = (t1 > 0 && t2 > 0) ? Mathf.Min(t1, t2) : Mathf.Max(t1, t2);

        if (timeToHit <= 0)
        {
            return null; // Столкновение в прошлом, невозможно
        }

        Vector3 perfectPredictionPoint = targetPos + targetVel * timeToHit;

        // Применяем "неточность" предсказания.
        // Мы смещаем точку прицеливания от текущей позиции цели в сторону идеальной точки упреждения.
        Vector3 finalPredictionPoint = targetPos.Lerp(perfectPredictionPoint, accuracy);

        return finalPredictionPoint;
    }

    /// <summary>
    /// Добавляет случайное смещение к точке прицеливания для имитации разброса.
    /// </summary>
    private Vector3 ApplyAimSpread(Vector3 targetPosition, float spreadRadius)
    {
        // Генерируем случайную точку в единичной сфере
        Vector3 randomOffset = new Vector3(
            (float)GD.RandRange(-1.0, 1.0),
            (float)GD.RandRange(-1.0, 1.0),
            (float)GD.RandRange(-1.0, 1.0)
        ).Normalized();

        // Масштабируем ее на случайное расстояние в пределах радиуса разброса
        randomOffset *= (float)GD.RandRange(0, spreadRadius);

        return targetPosition + randomOffset;
    }
}