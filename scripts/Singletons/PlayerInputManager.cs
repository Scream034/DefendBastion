using Godot;
using Game.Interfaces;

namespace Game.Singletons;

/// <summary>
/// Глобальный менеджер ввода, который перенаправляет действия игрока
/// текущему активному контроллеру камеры (ICameraController).
/// Является синглтоном (Autoload).
/// </summary>
public partial class PlayerInputManager : Node
{
    public static PlayerInputManager Instance { get; private set; }

    private ICameraController _activeController;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        // Подписываемся на глобальное событие тряски
        GlobalEvents.Instance.WorldShakeRequested += OnWorldShakeRequested;
    }

    /// <summary>
    /// Переключает активный контроллер камеры.
    /// </summary>
    /// <param name="newController">Новый контроллер для активации. Если null, ввод отключается.</param>
    public void SwitchController(ICameraController newController)
    {
        _activeController?.Deactivate();

        _activeController = newController;

        _activeController?.Activate();

        // Устанавливаем новую камеру как текущую для вьюпорта
        var newCamera = _activeController?.GetCamera();
        if (newCamera != null)
        {
            GetViewport().GetCamera3D().Current = false;
            newCamera.Current = true;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_activeController == null) return;

        ProcessMouseInput(@event);
        ProcessOwnerInput(@event);
    }

    public override void _Process(double delta)
    {
        // Передаем delta в контроллер камеры для плавного возврата тряски
        // Только если есть активный контроллер
        _activeController?.HandleMouseInput(Vector2.Zero, (float)delta);
    }

    /// <summary>
    /// Обрабатывает ввод мыши
    /// </summary>
    private void ProcessMouseInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && mouseMotion.Relative != Vector2.Zero)
        {
            _activeController?.HandleMouseInput(mouseMotion.Relative);
        }
    }

    /// <summary>
    /// Обрабатывает ввод владельца камеры
    /// </summary>
    private void ProcessOwnerInput(InputEvent @event)
    {
        if (_activeController?.GetCameraOwner() is IOwnerCameraController ownerCamera)
        {
            ownerCamera.HandleInput(@event);
        }
    }

    private void OnWorldShakeRequested(Vector3 origin, float strength, float maxRadius, float duration)
    {
        if (_activeController == null) return;

        var camera = _activeController.GetCamera();
        if (camera == null) return;

        if (IsShakeInRange(camera.GlobalPosition, origin, maxRadius))
        {
            float finalStrength = CalculateShakeStrength(camera.GlobalPosition, origin, strength, maxRadius);
            _activeController.ApplyShake(duration, finalStrength);
        }
    }

    /// <summary>
    /// Проверяет, находится ли камера в радиусе действия тряски
    /// </summary>
    private static bool IsShakeInRange(Vector3 cameraPosition, Vector3 origin, float maxRadius)
    {
        return cameraPosition.DistanceTo(origin) <= maxRadius;
    }

    /// <summary>
    /// Вычисляет финальную силу тряски с учетом расстояния
    /// </summary>
    private static float CalculateShakeStrength(Vector3 cameraPosition, Vector3 origin, float strength, float maxRadius)
    {
        float distance = cameraPosition.DistanceTo(origin);
        float falloff = 1.0f - (distance / maxRadius);
        return strength * falloff;
    }
}