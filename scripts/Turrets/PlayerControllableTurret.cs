using Game.Entity;
using Game.Interfaces;
using Game.Projectiles;
using Godot;

namespace Game.Turrets;

/// <summary>
/// Реализация турели, управляемой игроком.
/// Отвечает за считывание ввода игрока, плавное наведение орудия
/// и вызов методов стрельбы/перезарядки базового класса.
/// </summary>
public partial class PlayerControllableTurret : ControllableTurret
{
    public Player.Player PlayerController => CurrentController as Player.Player;

    public override void _PhysicsProcess(double delta)
    {
        if (PlayerController == null || _turretYaw == null || _turretPitch == null)
        {
            return;
        }

        // --- Логика наведения ---
        // 1. Получаем глобальную ориентацию камеры игрока.
        Basis targetGlobalBasis = PlayerController.GetPlayerHead().Camera.GlobalTransform.Basis;

        // 2. Преобразуем ее в локальную систему координат турели (относительно поворота корпуса турели).
        Basis localBasis = GlobalTransform.Basis.Inverse() * targetGlobalBasis;

        // 3. Извлекаем из локальной ориентации углы Эйлера (рыскание и тангаж).
        Vector3 targetEuler = localBasis.GetEuler();

        // 4. Ограничиваем углы в соответствии с лимитами турели.
        float targetYaw = _maxYawRad < 0 ? targetEuler.Y : Mathf.Clamp(targetEuler.Y, -_maxYawRad, _maxYawRad);
        float targetPitch = Mathf.Clamp(targetEuler.X, _minPitchRad, _maxPitchRad);

        // 5. Плавно интерполируем текущие углы поворота частей турели к целевым.
        float fDelta = (float)delta;
        _turretYaw.Rotation = _turretYaw.Rotation with { Y = Mathf.LerpAngle(_turretYaw.Rotation.Y, targetYaw, _aimSpeed * fDelta) };
        _turretPitch.Rotation = _turretPitch.Rotation with { X = Mathf.LerpAngle(_turretPitch.Rotation.X, targetPitch, _aimSpeed * fDelta) };

        // 6. Принудительно удерживаем позицию игрока в "кресле", чтобы избежать смещений из-за физики.
        PlayerController.GlobalPosition = _controlPoint.GlobalPosition;
    }

    public override void _Input(InputEvent @event)
    {
        if (PlayerController == null) return;

        if (@event.IsActionPressed("fire"))
        {
            Shoot();
        }
        else if (@event.IsActionPressed("reload"))
        {
            StartReload();
        }
        else if (@event.IsActionPressed("interact"))
        {
            ExitTurret();
            // NOTE: Помечаем событие как обработанное, чтобы игрок не начал тут же
            // взаимодействовать с этой же турелью снова.
            GetViewport().SetInputAsHandled();
        }
    }

    public void ShakeCamera()
    {
        PlayerController.GetPlayerHead().ShakeAsync((float)GD.RandRange(0.07f, 0.12f), (float)GD.RandRange(0.03f, 0.05f));
    }

    public override BaseProjectile CreateProjectile(Transform3D spawnPoint)
    {
        var projectile = base.CreateProjectile(spawnPoint);
        projectile.IgnoredEntities.Add(PlayerController);
        return projectile;
    }

}