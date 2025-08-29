using Godot;
using Game.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Singletons;
using Game.Entity;

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

    /// <summary>
    /// Сущность, которая выпустила этот снаряд.
    /// </summary>
    public LivingEntity Initiator { get; private set; }

    protected Timer lifetimeTimer;
    protected PhysicsRayQueryParameters3D rayQueryParams;
    protected CollisionShape3D collisionShape;

    public override void _Ready()
    {
        foreach (var child in GetChildren())
        {
            if (child is CollisionShape3D shape)
            {
                collisionShape = shape;
                break;
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
    public virtual void Initialize(LivingEntity initiator = null) // Изменен тип на Node3D для большей точности
    {
        Initiator = initiator;

        if (initiator != null)
        {
            IgnoredEntities.Add(initiator);
        }

        if (rayQueryParams == null)
        {
            rayQueryParams = PhysicsRayQueryParameters3D.Create(
                GlobalPosition,
                GlobalPosition,
                CollisionMask,
                [GetRid()]
            );
        }
        else
        {
            rayQueryParams.From = GlobalPosition;
            rayQueryParams.To = GlobalPosition;
        }
        lifetimeTimer.Start(Lifetime);
    }

    public override void _PhysicsProcess(double delta)
    {
        float travelDistance = Speed * (float)delta;
        Vector3 velocity = -GlobalTransform.Basis.Z * travelDistance;
        Vector3 nextPosition = GlobalPosition + velocity;

        rayQueryParams.From = GlobalPosition;
        rayQueryParams.To = nextPosition;

        var result = Constants.DirectSpaceState.IntersectRay(rayQueryParams);

        if (result.Count > 0)
        {
            var collider = result["collider"].As<Node>();
            if (!IgnoredEntities.Contains(collider))
            {
                OnHit(result).Wait();
                return;
            }
        }

        GlobalPosition = nextPosition;
    }

    protected virtual async Task HandleHitAndDamage(Godot.Collections.Dictionary hitInfo)
    {
        var collider = hitInfo["collider"].As<Node>();
        GlobalPosition = hitInfo["position"].AsVector3();
        if (collider is ICharacter damageable)
        {
            // <<--- [ИЗМЕНЕНИЕ 2] Передаем инициатора как источник урона!
            await damageable.DamageAsync(Damage, Initiator);
        }
    }

    protected virtual async Task OnHit(Godot.Collections.Dictionary hitInfo)
    {
        await HandleHitAndDamage(hitInfo);
        ProjectilePool.Return(this);
    }

    public virtual void ResetState()
    {
        Visible = true;
        SetProcess(true);
        SetPhysicsProcess(true);
        if (collisionShape != null) collisionShape.Disabled = false;

        IgnoredEntities.Clear();
        Initiator = null; // <<--- [ИЗМЕНЕНИЕ 3] Сбрасываем инициатора при возврате в пул

        if (lifetimeTimer == null)
        {
            lifetimeTimer = new() { OneShot = true };
            lifetimeTimer.Timeout += OnLifetimeTimeout;
            AddChild(lifetimeTimer);
        }
    }

    public virtual void Disable()
    {
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
        if (collisionShape != null) collisionShape.Disabled = true;

        lifetimeTimer.Stop();
        IgnoredEntities.Clear();
    }

    private void OnLifetimeTimeout()
    {
        ProjectilePool.Return(this);
    }
}