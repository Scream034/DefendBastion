using Godot;

namespace Game.Entity;

/// <summary>
/// Абстрактный базовый класс для всех живых сущностей, подверженных гравитации.
/// Инкапсулирует логику применения гравитации и базового движения.
/// </summary>
public abstract partial class MoveableEntity : LivingEntity
{
    [ExportGroup("Movement")]
    [Export] public float Speed { get; protected set; } = 5.0f;
    [Export] public float Acceleration { get; protected set; } = 4.0f;
    [Export] public float Deceleration { get; protected set; } = 4.0f;
    [Export] public float JumpVelocity { get; protected set; } = 4.5f;

    /// <summary>
    /// Этот метод вызывается каждый кадр физики.
    /// Он применяет гравитацию. Дочерние классы ДОЛЖНЫ вызывать base._PhysicsProcess(delta)
    /// в начале своего метода _PhysicsProcess.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        // Применяем гравитацию, если сущность не на полу.
        if (!IsOnFloor())
        {
            // Мы напрямую изменяем свойство Velocity,
            // дочерние классы будут работать с ним дальше.
            Velocity = Velocity with { Y = Velocity.Y - World.DefaultGravity * (float)delta };
        }
    }

    /// <summary>
    /// Вспомогательный метод для выполнения прыжка.
    /// Может быть вызван из дочерних классов (например, игроком).
    /// </summary>
    protected void Jump()
    {
        if (IsOnFloor())
        {
            Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
        }
    }
}