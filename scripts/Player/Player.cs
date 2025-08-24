using Godot;
using Game.Entity;
using Game.Interfaces;
using Game.Turrets;
using Game.Singletons;

namespace Game.Player;

public sealed partial class Player : LivingEntity, IOwnerCameraController, ITurretControllable
{
    [Signal]
    public delegate void OnInteractableDetectedEventHandler(Node3D interactable);

    public enum PlayerState
    {
        Normal,
        InTurret
    }

    [ExportGroup("Components")]
    [Export] private PlayerHead _head;
    [Export] private CollisionShape3D _collisionShape; // Ссылка на CollisionShape3D

    [ExportGroup("Movement")]
    [Export] public float Speed = 4.25f;
    [Export] public float JumpVelocity = 3f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05")] public float AirControl = 0.1f;
    [Export(PropertyHint.Range, "0, 20, 1")] public float Acceleration = 10f;
    [Export(PropertyHint.Range, "0, 20, 1")] public float Deceleration = 10f;
    [Export(PropertyHint.Range, "0, 20, 1")] public float BodyRotationSpeed = 5f;

    public PlayerState CurrentState { get; private set; } = PlayerState.Normal;
    private IInteractable _lastInteractable;
    private Vector3 _inputDir;
    private bool _jumpPressed;

    private Node _originalHeadParent;
    private ControllableTurret _currentTurret;

    public Player() : base(IDs.Player) { }

    public override void _Ready()
    {
        base._Ready();
        _originalHeadParent = _head.GetParent(); // Настоящий родитель головы игрока

        // Устанавливаем голову игрока как стартовый контроллер камеры
        PlayerInputManager.Instance.SwitchController(_head);

#if DEBUG
        if (_head == null) GD.PushError("Для игрока не был назначен PlayerHead.");
        if (_collisionShape == null) GD.PushError("Для игрока не был назначен CollisionShape3D.");
        if (Constants.DefaultGravity <= 0) GD.PushWarning($"Неверное использование гравитации: {Constants.DefaultGravity}");
#endif
    }

    public override void _PhysicsProcess(double delta)
    {
        SetBodyYaw(_head.Rotation.Y);

        // В турели физика игрока полностью отключается
        if (CurrentState == PlayerState.InTurret)
        {
            Velocity = Vector3.Zero;
            return;
        }

        ProcessNormal((float)delta);
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

    private void HandleMovement(in float delta)
    {
        var isOnFloor = IsOnFloor();
        Vector3 velocity = Velocity;

        if (!isOnFloor)
            velocity.Y -= Constants.DefaultGravity * delta;

        if (_jumpPressed && isOnFloor)
        {
            velocity.Y = JumpVelocity;
        }
        _jumpPressed = false;

        Vector3 direction = (_head.Transform.Basis * _inputDir).Normalized();
        Vector3 targetVelocity = direction * Speed;

        float currentAirControl = isOnFloor ? 1.0f : AirControl;
        float lerpSpeed = direction.IsZeroApprox() ? Deceleration : Acceleration;

        velocity.X = Mathf.Lerp(velocity.X, targetVelocity.X, lerpSpeed * currentAirControl * delta);
        velocity.Z = Mathf.Lerp(velocity.Z, targetVelocity.Z, lerpSpeed * currentAirControl * delta);

        Velocity = velocity;
        MoveAndSlide();
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

    public void EnterTurret(BaseTurret turret)
    {
        if (CurrentState != PlayerState.Normal || turret is not ControllableTurret controllableTurret) return;

        CurrentState = PlayerState.InTurret;
        _currentTurret = controllableTurret;
        _collisionShape.Disabled = true;
        UI.Instance.HideInteractionText();

    }

    public void ExitTurret(Vector3 exitPosition)
    {
        if (CurrentState != PlayerState.InTurret) return;

        GlobalPosition = exitPosition;
        _collisionShape.Disabled = false;
        _currentTurret = null;
        CurrentState = PlayerState.Normal;

        // Сообщаем менеджеру, что нужно вернуть управление голове игрока
        PlayerInputManager.Instance.SwitchController(_head);
    }

    public bool IsInTurret() => CurrentState == PlayerState.InTurret;

    public PlayerHead GetPlayerHead()
    {
        return _head;
    }

    public void HandleInput(in InputEvent @event)
    {
        // Управление вводом теперь централизовано в PlayerInputManager.
        // Оставляем только то, что касается движения.
        if (CurrentState == PlayerState.InTurret) return;

        var direction = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        _inputDir = new Vector3(direction.X, 0, direction.Y).Normalized();

        if (@event.IsActionPressed("jump"))
        {
            _jumpPressed = true;
        }
        else if (@event.IsActionPressed("interact") && _head.CurrentInteractable != null)
        {
            // Действие взаимодействия все еще инициируется здесь
            _head.CurrentInteractable.Interact(this);
        }
    }
}