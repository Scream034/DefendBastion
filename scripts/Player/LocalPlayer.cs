using Godot;
using Game.Entity;
using Game.Interfaces;
using Game.Turrets;
using Game.Singletons;
using System.Threading.Tasks;
using Game.UI;

namespace Game.Player;

public sealed partial class LocalPlayer : MoveableEntity, IOwnerCameraController, ITurretControllable
{
    public static LocalPlayer Instance { get; private set; } = null!;

    [Signal]
    public delegate void OnInteractableDetectedEventHandler(Node3D interactable);

    public enum PlayerState
    {
        Normal,
        InTurret,
        Freecam
    }

    [ExportGroup("Components")]
    [Export] public PlayerHead Head { get; private set; } = null!;
    [Export] private CollisionShape3D _collisionShape;

    [ExportGroup("Movement")]
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05")] public float AirControl = 0.1f;

    public PlayerState CurrentState { get; private set; } = PlayerState.Normal;

    /// <summary>
    /// Публичное свойство для доступа к текущей турели, в которой находится игрок.
    /// </summary>
    public ControllableTurret CurrentTurret { get; private set; }

    private IInteractable _lastInteractable;
    private Vector3 _inputDir;
    private bool _jumpPressed;
    private float _lastIntegrityPercent = 100f; // Для отслеживания изменений

    private readonly FreecamController _freecamController = new();

    public LocalPlayer()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        base._Ready();

        // Устанавливаем голову игрока как стартовый контроллер камеры
        PlayerInputManager.Instance.SwitchController(Head);

        RobotBus.Sys("NEURAL_OS v4.0.2: BOOT SEQUENCE COMPLETE");

#if DEBUG
        if (Head == null) GD.PushError("Для игрока не был назначен PlayerHead.");
        if (_collisionShape == null) GD.PushError("Для игрока не был назначен CollisionShape3D.");
        if (World.DefaultGravity <= 0) GD.PushWarning($"Неверное использование гравитации: {World.DefaultGravity}");
#endif
    }

    public override void _PhysicsProcess(double delta)
    {
        // В режиме Freecam мы полностью игнорируем стандартную физику
        if (CurrentState == PlayerState.Freecam)
        {
            ProcessFreecam((float)delta);
            return;
        }

        // Стандартная физика (гравитация) применяется только если мы НЕ в Freecam и НЕ в турели
        if (CurrentState != PlayerState.InTurret)
        {
            base._PhysicsProcess(delta); // Применяет гравитацию
        }

        SetBodyYaw(Head.Rotation.Y);

        if (CurrentState == PlayerState.InTurret)
        {
            Velocity = Vector3.Zero;
            return;
        }

        ProcessNormal((float)delta);
        MoveAndSlide();
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

    public void SetBodyYaw(in float yaw)
    {
        _collisionShape.Rotation = _collisionShape.Rotation with { Y = yaw };
    }

    private void ProcessNormal(float delta)
    {
        HandleMovement(delta);
        HandleInteractionUI();

        UpdateIntegrityLogic();
    }

    /// <summary>
    /// Логика отслеживания прочности и вывода в систему логов.
    /// </summary>
    private void UpdateIntegrityLogic()
    {
        float currentPercent = (Health / MaxHealth) * 100f;

        if (!Mathf.IsEqualApprox(currentPercent, _lastIntegrityPercent))
        {
            float diff = _lastIntegrityPercent - currentPercent;

            if (diff > 0) // Урон
            {
                // Просто кидаем сообщение в шину
                RobotBus.Warn($"HULL BREACH DETECTED: -{diff:F1}%");
                RobotBus.Sys($"REROUTING POWER TO SECTOR {GD.Randi() % 9}");
            }
            else // Ремонт
            {
                RobotBus.Sys($"REPAIR SEQUENCE: +{Mathf.Abs(diff):F1}%");
            }

            _lastIntegrityPercent = currentPercent;
        }
    }

    private void ProcessFreecam(float delta)
    {
        // Делегируем расчет движения контроллеру
        // Используем GlobalBasis головы, чтобы лететь туда, куда смотрим
        Vector3 motion = _freecamController.CalculateMovement(Head.GlobalTransform.Basis, delta);

        // Напрямую меняем позицию (Noclip)
        GlobalPosition += motion;

        // Скрываем UI взаимодействия в режиме полета
        ManagerUI.Instance.HideInteractionText();
    }

    private void HandleMovement(in float delta)
    {
        // 4. Упрощаем метод движения. Гравитация уже применена в base._PhysicsProcess.
        Vector3 velocity = Velocity;

        // Используем метод Jump() из базового класса
        if (_jumpPressed)
        {
            Jump();
            // Обновляем локальную `velocity`, т.к. Jump() изменил свойство `Velocity`
            velocity.Y = Velocity.Y;
        }
        _jumpPressed = false;

        Vector3 direction = (Head.Transform.Basis * _inputDir).Normalized();
        Vector3 targetVelocity = direction * Speed;

        float currentAirControl = IsOnFloor() ? 1.0f : AirControl;
        float lerpSpeed = direction.IsZeroApprox() ? Deceleration : Acceleration;

        velocity.X = Mathf.Lerp(velocity.X, targetVelocity.X, lerpSpeed * currentAirControl * delta);
        velocity.Z = Mathf.Lerp(velocity.Z, targetVelocity.Z, lerpSpeed * currentAirControl * delta);

        Velocity = velocity;
    }

    private void HandleInteractionUI()
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

    public void EnterTurret(ControllableTurret turret)
    {
        if (CurrentState != PlayerState.Normal) return;

        CurrentState = PlayerState.InTurret;
        CurrentTurret = turret;

        // Переключаем интерфейс на турельный (с глитчами и звуком)
        if (turret is PlayerControllableTurret playerTurret)
        {
            // Находим контроллер камеры, привязанный к этой турели
            // Обычно он находится в детях или экспортирован
            var camController = playerTurret.GetNode<TurretCameraController>("TurretCameraController");
            ManagerUI.Instance.SwitchToTurretMode(playerTurret, camController);
        }

        RobotBus.Net($"HANDSHAKE SEQUENCE: {turret.Name}");
        RobotBus.Net("REMOTE_LINK: ESTABLISHED");
    }

    public void ExitTurret(Vector3 exitPosition)
    {
        if (CurrentState != PlayerState.InTurret) return;

        GlobalPosition = exitPosition;
        CurrentTurret = null;
        CurrentState = PlayerState.Normal;

        ManagerUI.Instance.SwitchToPlayerMode();
        PlayerInputManager.Instance.SwitchController(Head);

        // При выходе из турели сбрасываем состояние здоровья для корректных логов
        _lastIntegrityPercent = Health / MaxHealth * 100f;
        RobotBus.Net("REMOTE_LINK: TERMINATED");
    }

    public bool IsInTurret() => CurrentState == PlayerState.InTurret;

    public PlayerHead GetPlayerHead()
    {
        return Head;
    }

    public void HandleInput(in InputEvent @event)
    {
        // 1. Переключение режима Freecam
        if (@event.IsActionPressed("freecam"))
        {
            ToggleFreecam();
            return;
        }

        // 2. Обработка ввода для Freecam
        if (CurrentState == PlayerState.Freecam)
        {
            _freecamController.HandleInput(@event);
            return; // Прерываем, чтобы не обрабатывать прыжки/движение
        }

        // 3. Стандартная обработка (если не в турели)
        if (CurrentState == PlayerState.InTurret) return;

        var direction = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        _inputDir = new Vector3(direction.X, 0, direction.Y).Normalized();

        if (@event.IsActionPressed("jump"))
        {
            _jumpPressed = true;
        }
        else if (@event.IsActionPressed("interact") && Head.CurrentInteractable != null)
        {
            Head.CurrentInteractable.Interact(this);
        }
    }

    private void ToggleFreecam()
    {
        if (CurrentState == PlayerState.Normal)
        {
            CurrentState = PlayerState.Freecam;
            Velocity = Vector3.Zero; // Сброс инерции

            // Отключаем коллизию, чтобы пролетать сквозь стены (опционально, но желательно для Freecam)
            _collisionShape.Disabled = true;

            GD.Print("Freecam Activated");
        }
        else if (CurrentState == PlayerState.Freecam)
        {
            CurrentState = PlayerState.Normal;
            _collisionShape.Disabled = false;
            GD.Print("Freecam Deactivated");
        }
        // Из турели в freecam переходить запрещаем (или нужна доп. логика выхода)
    }
}