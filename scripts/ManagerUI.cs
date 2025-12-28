#nullable enable

using Godot;
using Game.Turrets;

namespace Game;

using UI;

public sealed partial class ManagerUI : CanvasLayer
{
    public static ManagerUI Instance { get; private set; } = null!;

    [ExportGroup("HUD Layers")]
    [Export] public PlayerHUD? PlayerHUD { get; private set; }
    [Export] public TurretHUD? TurretHUD { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        // При старте убеждаемся, что турельный HUD выключен
        TurretHUD?.Hide();
        PlayerHUD?.Show();
    }

    /// <summary>
    /// Переключает интерфейс в режим боевой турели.
    /// </summary>
    public void SwitchToTurretMode(PlayerControllableTurret turret, TurretCameraController cameraController)
    {
        PlayerHUD?.Hide();
        // Запускаем процедуру подключения
        TurretHUD?.BootUp(turret, cameraController);
    }

    /// <summary>
    /// Возвращает интерфейс в стандартный режим робота.
    /// </summary>
    public void SwitchToPlayerMode()
    {
        TurretHUD?.Shutdown();
        PlayerHUD?.Show();
    }

    /// <summary>
    /// Прокси-метод для отображения текста взаимодействия в PlayerHUD.
    /// </summary>
    public void SetInteractionText(string text)
    {
        PlayerHUD?.SetInteraction(text);
    }

    public void HideInteractionText()
    {
        PlayerHUD?.SetInteraction(null);
    }
}