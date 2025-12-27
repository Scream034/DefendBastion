#nullable enable

using Godot;
using Game.Interfaces;
using System.Threading.Tasks;
using Game.Entity;

namespace Game.Turrets;

/// <summary>
/// Базовый класс для турели, которой может управлять сущность (Игрок или ИИ).
/// Отвечает за физическое вращение башни и ствола, вход/выход контроллера и стрельбу.
/// </summary>
public partial class ControllableTurret : ShootingTurret, IContainerEntity
{
    #region Nodes

    [ExportGroup("Nodes")]
    [Export] public Node3D TurretYaw { get; private set; } = null!;

    [Export] public Node3D TurretPitch { get; private set; } = null!;

    [ExportGroup("Points")]
    [Export] protected Marker3D? ControlPoint;

    [Export] protected Marker3D? ExitPoint;

    #endregion

    #region Settings

    [ExportGroup("Aiming Properties")]
    /// <summary>
    /// Скорость поворота турели в радианах в секунду.
    /// Рекомендуемые значения: 0.5 - 2.0 для тяжелой техники, 3.0+ для легкой.
    /// </summary>
    [Export(PropertyHint.Range, "0.1, 10.0, 0.1")]
    private float _aimSpeed = 0.16f;

    #endregion

    /// <summary>
    /// Текущий контроллер (пилот/стрелок), управляющий турелью.
    /// </summary>
    public virtual ITurretControllable? CurrentController { get; protected set; }

    // Целевые углы вращения (в радианах), устанавливаемые контроллером
    protected float TargetYawRad;
    protected float TargetPitchRad;

    public override void _Ready()
    {
        base._Ready();

        // Отключаем обработку по умолчанию для экономии ресурсов CPU.
        // Она будет включена только когда в турели кто-то есть.
        SetProcess(false);
        SetPhysicsProcess(false);
        SetProcessInput(false);

#if DEBUG
        if (TurretYaw == null) GD.PushError($"[{Name}] TurretYaw node is missing!");
        if (TurretPitch == null) GD.PushError($"[{Name}] TurretPitch node is missing!");
#endif
    }

    public override void _PhysicsProcess(double delta)
    {
        // Рассчитываем максимальный шаг поворота за этот кадр
        float rotationStep = _aimSpeed * (float)delta;

        RotateYaw(rotationStep);
        RotatePitch(rotationStep);
    }

    #region Rotation Logic

    /// <summary>
    /// Поворачивает башню по горизонтали (Yaw) к целевому углу с фиксированной скоростью.
    /// </summary>
    private void RotateYaw(float step)
    {
        float currentYaw = TurretYaw.Rotation.Y;

        // Находим кратчайшую разницу между текущим и целевым углом (-PI до PI).
        float diff = Mathf.AngleDifference(currentYaw, TargetYawRad);

        // Линейно двигаем текущий угол к (текущий + разница).
        // Это обеспечивает фиксированную угловую скорость и остановку точно в цели.
        float newYaw = Mathf.MoveToward(currentYaw, currentYaw + diff, step);

        TurretYaw.Rotation = TurretYaw.Rotation with { Y = newYaw };
    }

    /// <summary>
    /// Поворачивает ствол по вертикали (Pitch) к целевому углу с фиксированной скоростью.
    /// </summary>
    private void RotatePitch(float step)
    {
        float currentPitch = TurretPitch.Rotation.X;
        float diff = Mathf.AngleDifference(currentPitch, TargetPitchRad);
        float newPitch = Mathf.MoveToward(currentPitch, currentPitch + diff, step);

        TurretPitch.Rotation = TurretPitch.Rotation with { X = newPitch };
    }

    /// <summary>
    /// Устанавливает целевые углы для вращения. Вызывается из TurretCameraController или AI.
    /// </summary>
    /// <param name="yawRad">Целевой угол поворота башни (Y).</param>
    /// <param name="pitchRad">Целевой угол подъема ствола (X).</param>
    public void SetAimTarget(float yawRad, float pitchRad)
    {
        TargetYawRad = yawRad;
        TargetPitchRad = pitchRad;
    }

    #endregion

    #region Interaction Logic

    public override async Task<bool> DestroyAsync()
    {
        if (!await base.DestroyAsync()) return false;
        ExitTurret();
        return true;
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
        return CurrentController == null
            ? "Нажмите E, чтобы использовать турель"
            : "Нажмите E, чтобы покинуть турель";
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

        // Если точка выхода не задана, используем позицию турели
        Vector3 exitPos = ExitPoint?.GlobalPosition ?? GlobalPosition;
        controllerToExit.ExitTurret(exitPos);
    }

    public LivingEntity GetContainedEntity() => (LivingEntity)CurrentController!;

    #endregion
}