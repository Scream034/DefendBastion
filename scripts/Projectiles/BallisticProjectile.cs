using Game.Singletons;
using Godot;

namespace Game.Projectiles;

/// <summary>
/// TODO: Доделать класс BallisticProjectile
/// </summary>
[System.Obsolete("Необходиомо доделать класс")]
public partial class BallisticProjectile : BaseProjectile
{
    private Vector3 _velocity;

    public override void _Ready()
    {
        base._Ready();
        // Инициализация скорости
        _velocity = -Transform.Basis.Z * Speed;
    }

    public override void _PhysicsProcess(double delta)
    {
        var fDelta = (float)delta;

        _velocity += World.DefaultGravityVector * fDelta;

        Position += _velocity * fDelta;

        // Обновление направления
        if (_velocity.LengthSquared() > 0)
        {
            LookAt(Position + _velocity, Vector3.Up);
        }
    }
}