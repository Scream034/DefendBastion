using Game.Interfaces;
using Godot;

namespace Game.Projectiles;

public partial class BaseProjectile : Area3D
{
    [Export]
    public float Speed = 100f;
    [Export]
    public float Damage = 10f;
    [Export]
    public float Lifetime = 5f;

    protected SceneTreeTimer lifetimeTimer;

    public override void _Ready()
    {
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
        if (body is IDamageable damageable)
        {
            damageable.Damage(Damage);
        }

        QueueFree();
    }
}
