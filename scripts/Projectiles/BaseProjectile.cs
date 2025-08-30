using Godot;
using Game.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Singletons;
using Game.Entity;
using System.Linq;

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

    public PackedScene SourceScene { get; set; }
    public LivingEntity Initiator { get; private set; }
    public PhysicsRayQueryParameters3D RayQueryParams { get; private set; }

    protected Timer lifetimeTimer;
    protected CollisionShape3D collisionShape;

    public override void _Ready()
    {
        collisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

        if (collisionShape == null)
        {
            GD.PushError($"Снаряд '{Name}' не имеет дочернего узла CollisionShape3D! Физика работать не будет.");
            SetPhysicsProcess(false);
        }
    }

    /// <summary>
    /// Инициализирует снаряд ПОСЛЕ его добавления в сцену.
    /// </summary>
    public virtual void Initialize(LivingEntity initiator = null)
    {
        Initiator = initiator;

        RayQueryParams = PhysicsRayQueryParameters3D.Create(GlobalPosition, GlobalPosition, CollisionMask);

        if (initiator != null)
        {
            RayQueryParams.Exclude = [GetRid(), initiator.GetRid()];
        }
        else
        {
            RayQueryParams.Exclude = [GetRid()];
        }

        lifetimeTimer.Start(Lifetime);
    }

    public override async void _PhysicsProcess(double delta)
    {
        float travelDistance = Speed * (float)delta;
        Vector3 velocity = -GlobalTransform.Basis.Z * travelDistance;
        Vector3 nextPosition = GlobalPosition + velocity;

        RayQueryParams.From = GlobalPosition;
        RayQueryParams.To = nextPosition;

        var result = World.DirectSpaceState.IntersectRay(RayQueryParams);

        if (result.Count > 0)
        {
            await OnHit(result);
            return;
        }

        GlobalPosition = nextPosition;
    }

    protected virtual async Task HandleHitAndDamage(Godot.Collections.Dictionary hitInfo)
    {
        var collider = hitInfo["collider"].As<Node>();
        GlobalPosition = hitInfo["position"].AsVector3();
        if (collider is ICharacter damageable)
        {
            await damageable.DamageAsync(Damage, Initiator);
        }

        GD.Print($"{Name} hit {collider.Name}");
    }

    protected virtual async Task OnHit(Godot.Collections.Dictionary hitInfo)
    {
        await HandleHitAndDamage(hitInfo);
        ProjectilePool.Instance.Return(this);
    }

    public virtual void ResetState()
    {
        Visible = true;
        SetProcess(true);
        SetPhysicsProcess(true);
        if (collisionShape != null)
        {
            RayQueryParams.Exclude = [];
            collisionShape.Disabled = false;
        }

        Initiator = null;

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
        RayQueryParams.Exclude = [];
    }

    private void OnLifetimeTimeout()
    {
        ProjectilePool.Instance.Return(this);
    }
}