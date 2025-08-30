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

    public override BaseProjectile CreateProjectile(Transform3D spawnPoint, LivingEntity initiator = null)
    {
        // Инициатором снаряда теперь является сама турель (this),
        // а не игрок внутри нее. Таким образом, ИИ будет корректно реагировать на турель.
        var projectile = base.CreateProjectile(spawnPoint, this);

        if (PlayerController != null)
        {
            // Мы по-прежнему исключаем игрока из raycast'а снаряда, чтобы он не взорвался на нем.
            projectile.RayQueryParams.Exclude.Add(PlayerController.GetRid());
        }
        return projectile;
    }

    public override void EnterTurret(ITurretControllable entity)
    {
        base.EnterTurret(entity);

        // Передаем контроллеру все необходимые ему компоненты и параметры
        _cameraController.Initialize(this);

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