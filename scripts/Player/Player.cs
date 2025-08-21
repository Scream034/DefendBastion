using Godot;
using Game.Entity;
using Game.Interfaces;
using System;

namespace Game.Player;

public sealed partial class Player : LivingEntity
{
    [Signal]
    public delegate void OnInteractableDetectedEventHandler(Node3D interactable);

    [ExportGroup("Movement")]
    [Export] public float Speed = 5.0f;
    [Export] public float JumpVelocity = 4.5f;
    [Export(PropertyHint.Range, "0.0, 1.0, 0.05")] public float AirControl = 0.1f;
    [Export(PropertyHint.Range, "0, 20, 1")] public float Acceleration = 10f;
    [Export(PropertyHint.Range, "0, 20, 1")] public float Deceleration = 10f;

    [Export] private PlayerHead _head;

    /// <summary>
    /// Заморозить игрока полностью (движение, прыжок, взаимодействие).
    /// </summary>
    public bool Freezed { get; private set; }

    private Vector2 _inputDir = Vector2.Zero;
    private bool _jumpPressed = false;
    private IInteractable _lastInteractable;

    public Player() : base(IDs.Player) { }

#if DEBUG
    public override void _Ready()
    {
        base._Ready();
        if (_head == null)
        {
            GD.PushError("Для игрока не был назначен PlayerHead.");
        }
        if (Constants.DefaultGravity <= 0)
        {
            GD.PushWarning($"Неверное использование гравитации: {Constants.DefaultGravity}");
        }
    }
#endif

    public override void _Input(InputEvent @event)
    {
        // Проверяем нажатие прыжка.
        if (@event.IsActionPressed("jump"))
        {
            _jumpPressed = true;
        }
        else if (@event.IsActionPressed("interact") && _head.CurrentInteractable != null)
        {
            _head.CurrentInteractable.Interact(this);
        }
        else
        {
            // Обновляем направление движения, если нет ни одной нажатой клавиши
            _inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
            return;
        }

        _inputDir = Vector2.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        var fDelta = (float)delta;
        HandleMovement(fDelta);

        HandleInteractionUI();
    }

    private void HandleMovement(in float delta)
    {
        var isOnFloor = IsOnFloor();
        Vector3 velocity = Velocity;

        // Гравитация
        if (!isOnFloor)
            velocity.Y -= Constants.DefaultGravity * delta;

        // Прыжок
        if (_jumpPressed && isOnFloor)
        {
            velocity.Y = JumpVelocity;
            _jumpPressed = false; // Сбрасываем флаг прыжка
        }
        _jumpPressed = false; // Убедимся, что флаг сбрасывается, даже если не на земле

        // Горизонтальное движение
        Vector3 direction = (Transform.Basis * new Vector3(_inputDir.X, 0, _inputDir.Y)).Normalized();
        Vector3 targetVelocity = direction * Speed;

        // Определяем текущий контроль (на земле или в воздухе)
        float currentAirControl = isOnFloor ? 1.0f : AirControl;

        // Выбираем ускорение или замедление
        float lerpSpeed = direction.IsZeroApprox() ? Deceleration : Acceleration;

        // Применяем интерполяцию для плавности
        velocity.X = Mathf.Lerp(velocity.X, targetVelocity.X, lerpSpeed * currentAirControl * delta);
        velocity.Z = Mathf.Lerp(velocity.Z, targetVelocity.Z, lerpSpeed * currentAirControl * delta);

        Velocity = velocity;
        MoveAndSlide();
    }

    private void HandleInteractionUI()
    {
        var currentInteractable = _head.CurrentInteractable;

        // Если текущий объект для взаимодействия изменился
        if (currentInteractable != _lastInteractable)
        {
            if (currentInteractable != null)
            {
                // Показываем новый текст
                UI.Instance.SetInteractionText(currentInteractable.GetInteractionText());
            }
            else
            {
                // Прячем текст
                UI.Instance.HideInteractionText();
            }
        }

        _lastInteractable = currentInteractable;
    }

    public PlayerHead GetPlayerHead()
    {
        return _head;
    }

    public void SetFreezed(bool freezed)
    {
        Freezed = freezed;
        if (freezed)
        {
            Velocity = Vector3.Zero;
            _inputDir = Vector2.Zero;
            _jumpPressed = false;

            _lastInteractable = null;
            UI.Instance.HideInteractionText();

            SetProcessInput(false);
            SetPhysicsProcess(false);
        }
        else
        {
            _lastInteractable = null;
            SetProcessInput(true);
            SetPhysicsProcess(true);
        }
    }
}