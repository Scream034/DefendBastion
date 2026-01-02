using Godot;
using Game.Turrets;

namespace Game.UI;

public partial class TurretHUD : Control
{
    [ExportGroup("Visuals")]
    [Export] private ColorRect _uplinkShaderRect; 
    [Export] private AnimationPlayer _animPlayer;

    public override void _Ready()
    {
        Visible = false;
        SetProcess(false);
    }

    public void BootUp(PlayerControllableTurret turret, TurretCameraController camera)
    {
        Visible = true;
        RobotBus.Net("INIT: NEURAL_LINK_ESTABLISHED");
        RobotBus.Net("Handshake: ACKNOWLEDGED");
        
        _animPlayer.Play("UplinkSequence");
        SetProcess(true);
    }

    /// <summary>
    /// Метод для корректного завершения работы интерфейса турели.
    /// </summary>
    public void Shutdown()
    {
        Visible = false;
        SetProcess(false);
        // Можно добавить анимацию затухания перед выключением
    }

    public override void _Process(double delta)
    {
        // Логика шума при повороте (опционально)
    }
    
    public void OnShoot()
    {
        RobotBus.Net("HOST: FIRE_EXEC");
    }
}