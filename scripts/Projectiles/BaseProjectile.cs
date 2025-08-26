using Godot;
using Game.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Singletons;

namespace Game.Projectiles;

/// <summary>
/// Базовый класс для всех снарядов в игре.
/// Реализует движение и логику столкновений с использованием предсказывающего RayCast
/// для предотвращения "туннелирования" на высоких скоростях.
/// </summary>
public partial class BaseProjectile : Area3D
{
    [Export(PropertyHint.Range, "1.0, 1000.0, 1.0")]
    public float Speed = 300f;

    [Export]
    public float Damage = 10f;

    [Export(PropertyHint.Range, "0.1, 30.0, 0.1")]
    public float Lifetime = 5f;

    public readonly List<Node> IgnoredEntities = [];

    /// <summary>
    /// Ссылка на исходную сцену, из которой был создан этот объект.
    /// Необходимо для возврата в правильный пул.
    /// </summary>
    public PackedScene SourceScene { get; set; }

    protected Timer lifetimeTimer;
    protected PhysicsRayQueryParameters3D rayQueryParams;

    protected CollisionShape3D collisionShape;

    public override void _Ready()
    {
        // Новый, надежный код:
        // Проходимся по всем дочерним узлам и ищем первый, который является CollisionShape3D.
        foreach (var child in GetChildren())
        {
            if (child is CollisionShape3D shape)
            {
                collisionShape = shape;
                break; // Нашли то, что искали, выходим из цикла
            }
        }

        if (collisionShape == null)
        {
            GD.PushError($"Снаряд '{Name}' не имеет дочернего узла CollisionShape3D! Физика работать не будет.");
        }
    }

    /// <summary>
    /// Инициализирует снаряд ПОСЛЕ его добавления в сцену.
    /// </summary>
    public virtual void Initialize(Node initiator = null)
    {
        if (initiator != null)
        {
            IgnoredEntities.Add(initiator);
        }

        if (rayQueryParams == null)
        {
            // Создаем новый PhysicsRayQueryParameters3D
            rayQueryParams = PhysicsRayQueryParameters3D.Create(
                GlobalPosition,
                GlobalPosition,
                CollisionMask,
                [GetRid()]
            );
        }
        else
        {
            // Переиспользуем PhysicsRayQueryParameters3D для экономии ресурсов
            rayQueryParams.From = GlobalPosition;
            rayQueryParams.To = GlobalPosition;
        }
        lifetimeTimer.Start(Lifetime);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Вычисляем вектор перемещения за этот кадр
        float travelDistance = Speed * (float)delta;
        Vector3 velocity = -GlobalTransform.Basis.Z * travelDistance;
        Vector3 nextPosition = GlobalPosition + velocity;

        // Создаем параметры для нашего предсказывающего луча
        rayQueryParams.From = GlobalPosition;
        rayQueryParams.To = nextPosition;

        // Выполняем запрос
        var result = Constants.DirectSpaceState.IntersectRay(rayQueryParams);

        // Обрабатываем результат
        if (result.Count > 0)
        {
#if DEBUG
            GD.Print($"[{Name}] Столкновения: {result.Count}. {GetRid()}!");
#endif
            // СТОЛКНОВЕНИЕ ОБНАРУЖЕНО!
            var collider = result["collider"].As<Node>();
            if (!IgnoredEntities.Contains(collider))
            {
                // Вызываем нашу логику обработки попадания, передавая всю информацию
                OnHit(result).Wait();
                return;
            }
        }

        // Если столкновения не было, просто двигаем снаряд
        GlobalPosition = nextPosition;
    }

    /// <summary>
    /// Основная логика, выполняемая при успешном попадании.
    /// Этот метод отвечает за базовые действия: перемещение в точку хита и нанесение урона.
    /// Наследники ДОЛЖНЫ вызвать этот метод и ПОСЛЕ него решить, когда возвращаться в пул.
    /// </summary>
    protected virtual async Task HandleHitAndDamage(Godot.Collections.Dictionary hitInfo)
    {
        var collider = hitInfo["collider"].As<Node>();
        // Перемещаем снаряд точно в точку попадания
        GlobalPosition = hitInfo["position"].AsVector3();
        if (collider is IDamageable damageable)
        {
            await damageable.DamageAsync(Damage);
        }
        // НЕ ВОЗВРАЩАЕМ В ПУЛ ЗДЕСЬ!
    }

    /// <summary>
    /// Этот метод просто обертка.
    /// Классы-наследники должны его переопределять (override).
    /// </summary>
    protected virtual async Task OnHit(Godot.Collections.Dictionary hitInfo)
    {
        await HandleHitAndDamage(hitInfo);
        // Простой снаряд без звуков и эффектов вернется в пул сразу.
        ProjectilePool.Return(this);
    }

    /// <summary>
    /// Сбрасывает состояние снаряда до значений по умолчанию для переиспользования.
    /// Вызывается пулом перед выдачей снаряда.
    /// </summary>
    public virtual void ResetState()
    {
        Visible = true;
        SetProcess(true);
        SetPhysicsProcess(true);
        if (collisionShape != null) collisionShape.Disabled = false;

        IgnoredEntities.Clear();

        if (lifetimeTimer == null)
        {
            lifetimeTimer = new()
            {
                OneShot = true
            };
            lifetimeTimer.Timeout += OnLifetimeTimeout;
            AddChild(lifetimeTimer);
        }
    }

    /// <summary>
    /// Отключает снаряд перед возвратом в пул.
    /// </summary>
    public virtual void Disable()
    {
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
        if (collisionShape != null) collisionShape.Disabled = true;

        lifetimeTimer.Stop();
        IgnoredEntities.Clear();
    }

    /// <summary>
    /// Вызывается по истечении времени жизни снаряда.
    /// </summary>
    private void OnLifetimeTimeout()
    {
#if DEBUG
        GD.Print($"[{Name}] Время жизни истекло! {GetRid()}!");
#endif
        ProjectilePool.Return(this);
    }
}
