#nullable enable

using Godot;
using Game.Turrets;

namespace Game;

using Game.UI.HUD;

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
        TurretHUD?.HideHUD();
        PlayerHUD?.ShowHUD();
    }

    /// <summary>
    /// Переключает интерфейс в режим боевой турели.
    /// </summary>
    public void SwitchToTurretMode(PlayerControllableTurret turret)
    {
        PlayerHUD?.HideHUD();
        // Запускаем процедуру подключения
        TurretHUD?.ShowHUD(turret);
    }

    /// <summary>
    /// Возвращает интерфейс в стандартный режим робота.
    /// </summary>
    public void SwitchToPlayerMode()
    {
        TurretHUD?.HideHUD();
        PlayerHUD?.ShowHUD();
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