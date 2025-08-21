namespace Game.Entity;

using Godot;

public abstract partial class LinearMoveableEntity(LivingEntity.IDs id) : LivingEntity(id)
{
    [Export]
    public float Speed { get; private set; } = 2.0f;

    public override void _PhysicsProcess(double _)
    {
        Vector3 forwardDirection = Transform.Basis.Z;
        Velocity = forwardDirection * Speed;
        MoveAndSlide();
    }
}