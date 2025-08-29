using Godot;
using Game.Interfaces;

namespace Game.Turrets;

public abstract partial class ControllableTurret : ShootingTurret
{
    [ExportGroup("Nodes")]
    [Export] public Node3D TurretYaw { get; private set; }
    [Export] public Node3D TurretPitch { get; private set; }

    [ExportGroup("Aiming Properties")]
    [Export(PropertyHint.Range, "1, 20, 0.1")] private float _aimSpeed = 8f;

    [ExportGroup("Points")]
    [Export] protected Marker3D _controlPoint;
    [Export] protected Marker3D _exitPoint;

    public virtual ITurretControllable CurrentController { get; protected set; }

    // Целевые углы, которые вычисляет контроллер
    protected float targetYawRad;
    protected float targetPitchRad;

    public override void _Ready()
    {
        base._Ready();
        // Отключаем обработку по умолчанию для экономии ресурсов.
        SetProcess(false);
        SetPhysicsProcess(false);
        SetProcessInput(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Применяем плавное вращение к целевым углам
        float fDelta = (float)delta;
        TurretYaw.Rotation = TurretYaw.Rotation with { Y = Mathf.LerpAngle(TurretYaw.Rotation.Y, targetYawRad, _aimSpeed * fDelta) };
        TurretPitch.Rotation = TurretPitch.Rotation with { X = Mathf.LerpAngle(TurretPitch.Rotation.X, targetPitchRad, _aimSpeed * fDelta) };
    }

    /// <summary>
    /// Устанавливает целевые углы для вращения. Вызывается контроллером.
    /// </summary>
    public void SetAimTarget(float yawRad, float pitchRad)
    {
        targetYawRad = yawRad;
        targetPitchRad = pitchRad;
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
