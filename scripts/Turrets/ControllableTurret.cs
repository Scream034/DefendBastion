using Game.Interfaces;
using Godot;

namespace Game.Turrets;

/// <summary>
/// Базовый класс для управляемых турелей.
/// Обрабатывает общую логику входа/выхода, взаимодействия
/// и предоставляет базовые свойства для управления.
/// </summary>
public abstract partial class ControllableTurret : ShootingTurret
{
    [ExportGroup("Nodes")]
    /// <summary>
    /// Узел, отвечающий за поворот по горизонтали (вокруг оси Y).
    /// </summary>
    [Export] protected Node3D _turretYaw;
    /// <summary>
    /// Узел, отвечающий за наклон по вертикали (вокруг оси X).
    /// </summary>
    [Export] protected Node3D _turretPitch;

    [ExportGroup("Rotation Limits (Degrees)")]
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch { get; private set; } = -5f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch { get; private set; } = 5f;
    /// <summary>
    /// Если установлен < 0, то без ограничения
    /// </summary>
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw { get; private set; } = 90f;

    [ExportGroup("Aiming Properties")]
    /// <summary>
    /// Скорость и плавность наведения турели.
    /// </summary>
    [Export(PropertyHint.Range, "1, 20, 0.1")]
    protected float _aimSpeed = 5f;

    [ExportGroup("Points")]
    /// <summary>
    /// Точка управления турелью.
    /// </summary>
    [Export] protected Marker3D _controlPoint;
    /// <summary>
    /// Точка выхода из турели.
    /// </summary>
    [Export] protected Marker3D _exitPoint;

    /// <summary>
    /// Текущий контроллер турели. Null, если турель свободна.
    /// </summary>
    public virtual ITurretControllable CurrentController { get; protected set; }

    // NOTE: Кэшируем значения в радианах для производительности.
    protected float _minPitchRad, _maxPitchRad, _maxYawRad;

    public override void _Ready()
    {
        base._Ready();
        // NOTE: Отключаем обработку по умолчанию для экономии ресурсов.
        // Процессы будут включаться только когда контроллер сядет в турель.
        SetProcess(false);
        SetPhysicsProcess(false);
        SetProcessInput(false);

        _minPitchRad = Mathf.DegToRad(MinPitch);
        _maxPitchRad = Mathf.DegToRad(MaxPitch);
        _maxYawRad = Mathf.DegToRad(MaxYaw);
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

    /// <summary>
    /// Посадить контроллер в турель.
    /// </summary>
    /// <param name="entity">Контроллер</param>
    public virtual void EnterTurret(ITurretControllable entity)
    {
        CurrentController = entity;
        CurrentController.EnterTurret(this);

        // Активируем обработку, пока контроллер в турели.
        SetProcess(true);
        SetPhysicsProcess(true);
        SetProcessInput(true);
    }

    /// <summary>
    /// Высадить контроллер из турели.
    /// </summary>
    public virtual void ExitTurret()
    {
        if (CurrentController == null) return;

        CurrentController.ExitTurret(_exitPoint.GlobalPosition);
        CurrentController = null;

        // Деактивируем обработку для экономии ресурсов.
        SetProcess(false);
        SetPhysicsProcess(false);
        SetProcessInput(false);
    }
}
