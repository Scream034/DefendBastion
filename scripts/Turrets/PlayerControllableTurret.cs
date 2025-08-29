using Godot;
using Game.Projectiles;
using Game.Interfaces;
using Game.Singletons;
using Game.Entity;
using Game.Entity.AI;

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

    private Faction _originalFaction;

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
        var projectile = base.CreateProjectile(spawnPoint, PlayerController);
        if (PlayerController != null)
        {
            projectile.IgnoredEntities.Add(PlayerController);
        }
        return projectile;
    }

    public override void EnterTurret(ITurretControllable entity)
    {
        if (entity is not IFactionMember factionController) return;

        _originalFaction = Faction;
        Faction = factionController.Faction;
        
        base.EnterTurret(entity);

        // Передаем контроллеру все необходимые ему компоненты и параметры
        _cameraController.Initialize(this);

        // Сообщаем менеджеру, что нужно переключиться на камеру турели
        PlayerInputManager.Instance.SwitchController(_cameraController);
    }

    public override void ExitTurret()
    {
        base.ExitTurret();
        Faction = _originalFaction;
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
