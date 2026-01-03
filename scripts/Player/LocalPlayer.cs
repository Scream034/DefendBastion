#nullable enable

using Godot;
using Game.Entity;
using Game.Interfaces;
using Game.Turrets;
using Game.Singletons;
using System.Threading.Tasks;
using Game.UI;

namespace Game.Player;

/// <summary>
/// Основной класс управляемого игрока.
/// Реализует движение, взаимодействие с миром, вход в турели и режим свободной камеры.
/// </summary>
public sealed partial class LocalPlayer : MoveableEntity, IOwnerCameraController, ITurretControllable
{
    #region Singleton
    public static LocalPlayer Instance { get; private set; } = null!;
    #endregion

    #region Constants & Input

    #endregion

    #region Signals

    [Signal]
    public delegate void OnInteractableDetectedEventHandler(Node3D interactable);

    #endregion

    #region Configuration

    [ExportGroup("Components")]
    [Export] public PlayerHead Head { get; private set; } = null!;
    [Export] private CollisionShape3D _collisionShape = null!;

    [ExportGroup("Movement Settings")]
    [ExportSubgroup("Air Physics")]
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05")] public float AirControl { get; private set; } = 0.1f;

    [ExportGroup("Debug & Tools")]
    [Export] private bool _enableDebugLogs = true;

    #endregion

    #region State Definitions

    public enum PlayerState
    {
        /// <summary>Стандартное передвижение пешком.</summary>
        Normal,
        /// <summary>Игрок управляет турелью (физика отключена).</summary>
        InTurret,
        /// <summary>Свободный полет камеры (Noclip).</summary>
        Freecam
    }

    /// <summary>Текущее состояние игрока.</summary>
    public PlayerState CurrentState { get; private set; } = PlayerState.Normal;

    /// <summary>Ссылка на турель, которой в данный момент управляет игрок.</summary>
    public ControllableTurret? CurrentTurret { get; private set; }

    #endregion

    #region Internal Fields

    private IInteractable? _lastInteractable;
    private Vector3 _inputDir;
    private bool _jumpPressed;

    // Состояние для отслеживания изменений здоровья (Observer pattern implementation inside Loop)
    private float _lastIntegrityPercent = 100f;

    // Контроллер свободной камеры (предполагается, что класс существует в контексте проекта)
    private readonly FreecamController _freecamController = new();

    #endregion

    #region Lifecycle

    public LocalPlayer()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        base._Ready();

        ValidateDependencies();
        InitializeSystem();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        switch (CurrentState)
        {
            case PlayerState.Normal:
                ProcessNormalState(dt);
                break;

            case PlayerState.InTurret:
                ProcessTurretState();
                break;

            case PlayerState.Freecam:
                ProcessFreecamState(dt);
                break;
        }

        // Обновляем вращение тела по Y только если мы не в свободном полете
        if (CurrentState != PlayerState.Freecam)
        {
            UpdateBodyRotation();
        }
    }

    public override async Task<bool> DestroyAsync()
    {
        var isDestroyed = await base.DestroyAsync();

        if (isDestroyed)
        {
            PlayerInputManager.Instance.SwitchController(null);
            CurrentTurret?.ExitTurret();
        }

        return isDestroyed;
    }

    #endregion

    #region State Processing Logic

    /// <summary>
    /// Логика стандартного состояния: физика, гравитация, движение, взаимодействие.
    /// </summary>
    private void ProcessNormalState(float delta)
    {
        // Базовая физика (гравитация) из MoveableEntity
        base._PhysicsProcess(delta);

        HandleMovementPhysics(delta);
        HandleInteractionRaycast();
        MonitorHullIntegrity();

        MoveAndSlide();
    }

    /// <summary>
    /// Логика состояния в турели: игрок зафиксирован, физика отключена.
    /// </summary>
    private void ProcessTurretState()
    {
        Velocity = Vector3.Zero;
        // Здесь можно добавить обновление специфичного UI, если требуется каждый кадр
    }

    /// <summary>
    /// Логика свободной камеры: полет сквозь стены, игнорирование гравитации.
    /// </summary>
    private void ProcessFreecamState(float delta)
    {
        // Вычисляем движение на основе базиса камеры (куда смотрим - туда летим)
        Vector3 motion = _freecamController.CalculateMovement(Head.GlobalTransform.Basis, delta);
        GlobalPosition += motion;

        ManagerUI.Instance.HideInteractionText();
    }

    #endregion

    #region Input Handling

    /// <summary>
    /// Централизованный обработчик ввода. Маршрутизирует события в зависимости от состояния.
    /// </summary>
    public void HandleInput(in InputEvent @event)
    {
        // Глобальные переключатели
        if (@event.IsActionPressed(Constants.ActionFreecamToggle))
        {
            ToggleFreecam();
            return;
        }

        // Ввод в зависимости от состояния
        switch (CurrentState)
        {
            case PlayerState.Freecam:
                _freecamController.HandleInput(@event);
                break;

            case PlayerState.Normal:
                HandleNormalInput(@event);
                break;

            case PlayerState.InTurret:
                // Ввод в турели обрабатывается самой турелью через PlayerInputManager
                break;
        }
    }

    private void HandleNormalInput(InputEvent @event)
    {
        // Вектор движения
        var direction = Input.GetVector(Constants.ActionMoveLeft, Constants.ActionMoveRight, Constants.ActionMoveForward, Constants.ActionMoveBackward);
        _inputDir = new Vector3(direction.X, 0, direction.Y).Normalized();

        // Действия
        if (@event.IsActionPressed(Constants.ActionJump))
        {
            _jumpPressed = true;
        }
        else if (@event.IsActionPressed(Constants.ActionInteract))
        {
            TryInteract();
        }
    }

    #endregion

    #region Movement & Physics Implementation

    private void HandleMovementPhysics(float delta)
    {
        Vector3 velocity = Velocity;

        // Обработка прыжка
        if (_jumpPressed)
        {
            Jump();
            // Получаем обновленный Y после прыжка
            velocity.Y = Velocity.Y;
            _jumpPressed = false;
        }

        // Расчет целевой скорости
        Vector3 direction = (Head.Transform.Basis * _inputDir).Normalized();
        Vector3 targetVelocity = direction * Speed;

        // Расчет инерции (земля vs воздух)
        float currentControl = IsOnFloor() ? 1.0f : AirControl;
        float lerpSpeed = direction.IsZeroApprox() ? Deceleration : Acceleration;

        velocity.X = Mathf.Lerp(velocity.X, targetVelocity.X, lerpSpeed * currentControl * delta);
        velocity.Z = Mathf.Lerp(velocity.Z, targetVelocity.Z, lerpSpeed * currentControl * delta);

        Velocity = velocity;
    }

    private void UpdateBodyRotation()
    {
        if (_collisionShape != null)
        {
            _collisionShape.Rotation = _collisionShape.Rotation with { Y = Head.Rotation.Y };
        }
    }

    #endregion

    #region Interaction System

    private void HandleInteractionRaycast()
    {
        var currentInteractable = Head.CurrentInteractable;

        if (currentInteractable != _lastInteractable)
        {
            if (currentInteractable != null)
            {
                ManagerUI.Instance.SetInteractionText(currentInteractable.GetInteractionText());
            }
            else
            {
                ManagerUI.Instance.HideInteractionText();
            }
        }
        _lastInteractable = currentInteractable;
    }

    private void TryInteract()
    {
        if (Head.CurrentInteractable != null)
        {
            Head.CurrentInteractable.Interact(this);
        }
    }

    #endregion

    #region Public API (Turret & Camera Control)

    /// <summary>
    /// Вход игрока в управляемую турель.
    /// </summary>
    public void EnterTurret(ControllableTurret turret)
    {
        if (CurrentState != PlayerState.Normal) return;

        CurrentState = PlayerState.InTurret;
        CurrentTurret = turret;

        // Переключаем UI и эффекты
        if (turret is PlayerControllableTurret playerTurret)
        {
            ManagerUI.Instance.SwitchToTurretMode(playerTurret);
        }

        RobotBus.Net($"HANDSHAKE SEQUENCE: {turret.Name}");
        RobotBus.Net("REMOTE_LINK: ESTABLISHED");
    }

    /// <summary>
    /// Выход из турели в указанную позицию.
    /// </summary>
    public void ExitTurret(Vector3 exitPosition)
    {
        if (CurrentState != PlayerState.InTurret) return;

        // Телепортация в точку выхода
        GlobalPosition = exitPosition;
        Velocity = Vector3.Zero; // Сброс инерции

        CurrentTurret = null;
        CurrentState = PlayerState.Normal;

        // Восстановление управления
        ManagerUI.Instance.SwitchToPlayerMode();
        PlayerInputManager.Instance.SwitchController(Head);

        // Синхронизация состояния здоровья для логов (чтобы не спамило разницу после выхода)
        SynchronizeHullIntegrity();

        RobotBus.Net("REMOTE_LINK: TERMINATED");
    }

    public bool IsInTurret() => CurrentState == PlayerState.InTurret;

    public PlayerHead GetPlayerHead() => Head;

    #endregion

    #region Helpers & Systems

    private void InitializeSystem()
    {
        // Активируем управление головой
        PlayerInputManager.Instance.SwitchController(Head);

        if (_enableDebugLogs)
        {
            RobotBus.Sys("NEURAL_OS v4.0.2: BOOT SEQUENCE COMPLETE");
        }
    }

    private void ValidateDependencies()
    {
#if DEBUG
        if (Head == null) GD.PushError($"[{nameof(LocalPlayer)}] PlayerHead не назначен!");
        if (_collisionShape == null) GD.PushError($"[{nameof(LocalPlayer)}] CollisionShape3D не назначен!");
        if (World.DefaultGravity <= 0) GD.PushWarning($"[{nameof(LocalPlayer)}] Гравитация странная: {World.DefaultGravity}");
#endif
    }

    private void ToggleFreecam()
    {
        if (CurrentState == PlayerState.Normal)
        {
            CurrentState = PlayerState.Freecam;
            Velocity = Vector3.Zero;
            _collisionShape.Disabled = true; // Отключаем коллизию для пролета сквозь стены

            if (_enableDebugLogs) GD.Print("SYS: Freecam Activated");
        }
        else if (CurrentState == PlayerState.Freecam)
        {
            CurrentState = PlayerState.Normal;
            _collisionShape.Disabled = false;

            if (_enableDebugLogs) GD.Print("SYS: Freecam Deactivated");
        }
        // Выход в Freecam из турели запрещен дизайном (нужно сначала выйти из турели)
    }

    private void MonitorHullIntegrity()
    {
        float currentPercent = Health / MaxHealth * 100f;

        if (!Mathf.IsEqualApprox(currentPercent, _lastIntegrityPercent))
        {
            float diff = _lastIntegrityPercent - currentPercent;

            if (diff > 0) // Урон
            {
                RobotBus.Warn($"HULL BREACH DETECTED: -{diff:F1}%");
                // Эффект "перенаправления энергии" для атмосферы
                RobotBus.Sys($"REROUTING POWER TO SECTOR {GD.Randi() % 9}");
            }
            else // Ремонт
            {
                RobotBus.Sys($"REPAIR SEQUENCE: +{Mathf.Abs(diff):F1}%");
            }

            _lastIntegrityPercent = currentPercent;
        }
    }

    private void SynchronizeHullIntegrity()
    {
        _lastIntegrityPercent = Health / MaxHealth * 100f;
    }

    #endregion
}