using Godot;

namespace Game.UI;

public partial class HUDInertia : Control
{
    [Export] public float DragIntensity = 5.0f;
    [Export] public float ReturnSpeed = 30.0f;

    private Vector2 _targetOffset = Vector2.Zero;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            // Смещаем интерфейс в сторону, противоположную движению мыши
            _targetOffset -= mm.Relative * DragIntensity * 0.01f;
        }
    }

    public override void _Process(double delta)
    {
        // Плавно возвращаем к центру (0,0)
        _targetOffset = _targetOffset.Lerp(Vector2.Zero, (float)delta * ReturnSpeed);
        
        // Ограничиваем смещение, чтобы интерфейс не улетел за экран
        _targetOffset = _targetOffset.LimitLength(50.0f);
        
        Position = _targetOffset;
    }
}