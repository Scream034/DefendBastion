using Godot;

namespace Game.Turrets;

/// <summary>
/// Класс турели, управляемой игроком.
/// </summary>
public partial class PlayerControllableTurret : ShootingTurret
{
    [ExportGroup("Player Control")]
    [Export]
    private Node3D _turretYaw; // Узел, отвечающий за поворот по горизонтали
    [Export]
    private Node3D _turretPitch; // Узел, отвечающий за наклон по вертикали
    [Export]
    private Camera3D _turretCamera;
    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    private float _mouseSensitivity = 0.1f;
    [Export]
    private float _minPitch = -60.0f;
    [Export]
    private float _maxPitch = 30.0f;

    private Player.Player _currentPlayer;
    public bool IsPlayerControlling { get; private set; } = false;

    public override void _Ready()
    {
        base._Ready();
        if (_turretCamera != null)
        {
            _turretCamera.Current = false;
        }
        // Отключаем обработку ввода по умолчанию
        SetProcessInput(false);
    }

    public override void Interact(Player.Player character)
    {
        if (IsPlayerControlling)
        {
            ExitTurret();
        }
        else
        {
            EnterTurret(character);
        }
    }

    public override string GetInteractionText()
    {
        return IsPlayerControlling ? "Нажмите E, чтобы покинуть турель" : "Нажмите E, чтобы использовать турель";
    }

    public void EnterTurret(Player.Player player)
    {
        if (IsPlayerControlling || player == null) return;

        _currentPlayer = player;
        IsPlayerControlling = true;

        // Отключаем управление и видимость самого игрока
        _currentPlayer.SetFreezed(true);
        _currentPlayer.Visible = false;

        if (_turretCamera != null)
        {
            _turretCamera.Current = true;
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
        SetProcessInput(true);
    }

    public void ExitTurret()
    {
        if (!IsPlayerControlling) return;

        if (_turretCamera != null)
        {
            _turretCamera.Current = false;
        }

        // Возвращаем управление и видимость игроку
        _currentPlayer.SetFreezed(false);
        _currentPlayer.Visible = true;
        _currentPlayer = null;
        IsPlayerControlling = false;

        Input.MouseMode = Input.MouseModeEnum.Captured;
        SetProcessInput(false);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsPlayerControlling) return;

        // Прицеливание мышью
        if (@event is InputEventMouseMotion mouseMotion)
        {
            Aim(mouseMotion.Relative);
        }
        // Стрельба
        else if (@event.IsActionPressed("fire")) // Предполагается, что у вас есть InputMap "fire"
        {
            Shoot();
        }
        // Перезарядка
        else if (@event.IsActionPressed("reload")) // InputMap "reload"
        {
            ForceReload();
        }
        // Выход из турели
        else if (@event.IsActionPressed("interact")) // InputMap "interact"
        {
            ExitTurret();
            // "Съедаем" событие, чтобы не было двойного срабатывания
            GetViewport().SetInputAsHandled();
        }
    }

    private void Aim(Vector2 mouseRelative)
    {
        _turretYaw?.RotateY(Mathf.DegToRad(-mouseRelative.X * _mouseSensitivity));

        if (_turretPitch != null)
        {
            float pitchChange = -mouseRelative.Y * _mouseSensitivity;
            float newPitch = Mathf.Clamp(_turretPitch.RotationDegrees.X + pitchChange, _minPitch, _maxPitch);
            _turretPitch.RotationDegrees = new Vector3(newPitch, _turretPitch.RotationDegrees.Y, _turretPitch.RotationDegrees.Z);
        }
    }

    public void ForceReload()
    {
        if (CurrentAmmo < MaxAmmo)
        {
            GD.Print("Reloading...");
            Reload(MaxAmmo - CurrentAmmo); // Полная перезарядка
        }
    }
}