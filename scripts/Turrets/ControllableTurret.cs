using Godot;
using Game.Interfaces;

namespace Game.Turrets;

public abstract partial class ControllableTurret : ShootingTurret
{
    [ExportGroup("Points")]
    [Export] protected Marker3D _controlPoint;
    [Export] protected Marker3D _exitPoint;

    public virtual ITurretControllable CurrentController { get; protected set; }

    public override void _Ready()
    {
        base._Ready();
        // Отключаем обработку по умолчанию для экономии ресурсов.
        SetProcess(false);
        SetPhysicsProcess(false);
        SetProcessInput(false);
    }

    public override void Interact(ITurretControllable entity)
    {
        if (CurrentController == null && entity != null)
        {
            EnterTurret(entity);
        }
        else if (CurrentController == entity)
        {
            ExitTurret();
        }
    }

    public override string GetInteractionText()
    {
        return CurrentController == null ? "Нажмите E, чтобы использовать турель" : "Нажмите E, чтобы покинуть турель";
    }

    public virtual void EnterTurret(ITurretControllable entity)
    {
        CurrentController = entity;
        CurrentController.EnterTurret(this);
    }

    public virtual void ExitTurret()
    {
        if (CurrentController == null) return;

        var controllerToExit = CurrentController;
        CurrentController = null;

        // Важно сначала высадить контроллер, а потом переключить камеру
        controllerToExit.ExitTurret(_exitPoint.GlobalPosition);
    }
}