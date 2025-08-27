using Godot;
using Game.Projectiles;
using Game.Interfaces;
using Game.Singletons;
using Game.Entity;

namespace Game.Turrets;

/// <summary>
/// Реализация турели, управляемой игроком.
/// Теперь в основном служит для связи между игроком и турелью,
/// а также для специфичных для игрока эффектов, таких как тряска камеры.
/// </summary>
public partial class PlayerControllableTurret : ControllableTurret, IOwnerCameraController
{
    [ExportGroup("Components")]
    [Export] private TurretCameraController _cameraController;

    public Player.Player PlayerController => CurrentController as Player.Player;

    public override void _PhysicsProcess(double delta)
    {
        // Вызываем базовый метод для вращения
        base._PhysicsProcess(delta);

        // Удерживаем игрока в кресле
        if (PlayerController != null && _controlPoint != null)
        {
            PlayerController.GlobalPosition = _controlPoint.GlobalPosition;
        }
    }

    public void ShakeCamera()
    {
        // Обратите внимание, что трясти нужно камеру игрока, а не турели.
        // Или можно добавить тряску и камере турели.
        // Для примера оставим тряску головы игрока.
        _cameraController.ShakeAsync((float)GD.RandRange(0.09f, 0.15f), (float)GD.RandRange(0.05f, 0.07f));
    }

    public override BaseProjectile CreateProjectile(Transform3D spawnPoint, LivingEntity initiator = null)
    {
        var projectile = base.CreateProjectile(spawnPoint, PlayerController);
        if (PlayerController != null)
        {
            projectile.IgnoredEntities.Add(PlayerController);
        }
        return projectile;
    }

    public override void EnterTurret(ITurretControllable entity)
    {
        base.EnterTurret(entity);

        // Передаем контроллеру все необходимые ему компоненты и параметры
        _cameraController.Initialize(this, TurretYaw, TurretPitch, BarrelEnd);

        // Сообщаем менеджеру, что нужно переключиться на камеру турели
        PlayerInputManager.Instance.SwitchController(_cameraController);
    }

    public void HandleInput(in InputEvent @event)
    {
        if (@event.IsActionPressed("interact"))
        {
            ExitTurret();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("fire"))
        {
            Shoot();
        }
        else if (@event.IsActionPressed("reload"))
        {
            StartReload();
        }
    }
}