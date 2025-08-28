using Godot;

namespace Game.Singletons;

/// <summary>
/// Глобальный хаб для игровых событий. Позволяет разным системам общаться,
/// не имея прямых ссылок друг на друга (паттерн "Издатель-подписчик").
/// </summary>
public partial class GlobalEvents : Node
{
    public static GlobalEvents Instance { get; private set; }

    /// <summary>
    /// Событие, которое возникает, когда в мире происходит что-то, вызывающее тряску (взрыв, выстрел и т.д.).
    /// </summary>
    /// <param name="origin">Эпицентр события в глобальных координатах.</param>
    /// <param name="strength">Базовая сила тряски в эпицентре.</param>
    /// <param name="maxRadius">Максимальный радиус, на котором ощущается тряска.</param>
    /// <param name="duration">Длительность тряски в секундах.</param>
    [Signal]
    public delegate void WorldShakeRequestedEventHandler(Vector3 origin, float strength, float maxRadius, float duration);

    public override void _EnterTree()
    {
        Instance = this;
    }

    /// <summary>
    /// Публичный метод для вызова события тряски из любого места в коде.
    /// </summary>
    public void RequestWorldShake(Vector3 origin, float strength, float maxRadius, float duration)
    {
        EmitSignal(SignalName.WorldShakeRequested, origin, strength, maxRadius, duration);
    }
}