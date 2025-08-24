using Godot;
using Game.Interfaces;
using Game.Turrets;

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

        // Перенаправляем движение мыши
        if (@event is InputEventMouseMotion mouseMotion)
        {
            _activeController.HandleMouseInput(mouseMotion.Relative);
        }

        // Централизованная обработка действий, которые могут исходить от разных контроллеров (Проверка на null через интерфейс)
        if (_activeController.GetCameraOwner() is IOwnerCameraController ownerCamera)
        {
            ownerCamera.HandleInput(@event);
        }
    }
}