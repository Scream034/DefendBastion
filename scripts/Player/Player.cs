using Godot;
using Game.Entity;
using Game.Interfaces;
using Game.Turrets;
using Game.Singletons;
using System.Threading.Tasks;

namespace Game.Player;

public sealed partial class Player : MoveableEntity, IOwnerCameraController, ITurretControllable
{
    [Signal]
    public delegate void OnInteractableDetectedEventHandler(Node3D interactable);

    public enum PlayerState
    {
        Normal,
        InTurret,
        Freecam
    }

    [ExportGroup("Components")]
    [Export] private PlayerHead _head;
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

    private readonly FreecamController _freecamController = new();

    public override void _Ready()
    {
        base._Ready();

        // Устанавливаем голову игрока как стартовый контроллер камеры
        PlayerInputManager.Instance.SwitchController(_head);

#if DEBUG
        if (_head == null) GD.PushError("Для игрока не был назначен PlayerHead.");
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

        SetBodyYaw(_head.Rotation.Y);

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
    }

    private void ProcessFreecam(float delta)
    {
        // Делегируем расчет движения контроллеру
        // Используем GlobalBasis головы, чтобы лететь туда, куда смотрим
        Vector3 motion = _freecamController.CalculateMovement(_head.GlobalTransform.Basis, delta);

        // Напрямую меняем позицию (Noclip)
        GlobalPosition += motion;

        // Скрываем UI взаимодействия в режиме полета
        UI.Instance.HideInteractionText();
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

        Vector3 direction = (_head.Transform.Basis * _inputDir).Normalized();
        Vector3 targetVelocity = direction * Speed;

        float currentAirControl = IsOnFloor() ? 1.0f : AirControl;
        float lerpSpeed = direction.IsZeroApprox() ? Deceleration : Acceleration;

        velocity.X = Mathf.Lerp(velocity.X, targetVelocity.X, lerpSpeed * currentAirControl * delta);
        velocity.Z = Mathf.Lerp(velocity.Z, targetVelocity.Z, lerpSpeed * currentAirControl * delta);

        Velocity = velocity;
    }

    private void HandleInteractionUI()
    {
        var currentInteractable = _head.CurrentInteractable;
        if (currentInteractable != _lastInteractable)
        {
            if (currentInteractable != null)
            {
                UI.Instance.SetInteractionText(currentInteractable.GetInteractionText());
            }
            else
            {
                UI.Instance.HideInteractionText();
            }
        }
        _lastInteractable = currentInteractable;
    }

    public void EnterTurret(ControllableTurret turret)
    {
        if (CurrentState != PlayerState.Normal) return;

        CurrentState = PlayerState.InTurret;
        CurrentTurret = turret;
        UI.Instance.HideInteractionText();

        GD.Print("Player enter turret");
    }

    public void ExitTurret(Vector3 exitPosition)
    {
        if (CurrentState != PlayerState.InTurret) return;

        GlobalPosition = exitPosition;
        CurrentTurret = null;
        CurrentState = PlayerState.Normal;

        // Сообщаем менеджеру, что нужно вернуть управление голове игрока
        PlayerInputManager.Instance.SwitchController(_head);

        GD.Print("Player exit turret");
    }

    public bool IsInTurret() => CurrentState == PlayerState.InTurret;

    public PlayerHead GetPlayerHead()
    {
        return _head;
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
        else if (@event.IsActionPressed("interact") && _head.CurrentInteractable != null)
        {
            _head.CurrentInteractable.Interact(this);
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