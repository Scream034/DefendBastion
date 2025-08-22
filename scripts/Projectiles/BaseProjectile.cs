using Game.Interfaces;
using Godot;
using System.Collections.Generic;

namespace Game.Projectiles;

public partial class BaseProjectile : Area3D
{
    [Export]
    public float Speed = 100f;
    [Export]
    public float Damage = 10f;
    [Export]
    public float Lifetime = 5f;

    public readonly List<Node> IgnoredEntities = [];

    protected SceneTreeTimer lifetimeTimer;

    public virtual void Initialize(Node initiator = null)
    {
        if (initiator != null)
        {
            IgnoredEntities.Add(initiator);
        }

        BodyEntered += OnBodyEntered;

        lifetimeTimer = Constants.Tree.CreateTimer(Lifetime);
        lifetimeTimer.Timeout += QueueFree;
    }

    public override void _PhysicsProcess(double delta)
    {
        Position += -Transform.Basis.Z * Speed * (float)delta;
    }

    public virtual void OnBodyEntered(Node body)
    {
        if (IgnoredEntities.Contains(body))
        {
            return;
        }
        else if (body is IDamageable damageable)
        {
            damageable.Damage(Damage);
        }

        QueueFree();
    }
}
