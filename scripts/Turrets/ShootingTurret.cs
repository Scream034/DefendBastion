using Godot;
using Game.Projectiles;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для стреляющих турелей.
/// Определяет, *как* турель стреляет, но не *когда*.
/// </summary>
public abstract partial class ShootingTurret : BaseTurret
{
    [ExportGroup("Shooting")]
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    protected PackedScene ProjectileScene { get; set; }

    [Export]
    public int MaxAmmo { get; set; } = 100;

    [Export]
    public int CurrentAmmo { get; set; }

    private float _fireRate = 1.0f;
    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float FireRate // Выстрелов в секунду
    {
        get => _fireRate;
        set
        {
            _fireRate = value;
            if (_cooldownTimer != null)
            {
                _cooldownTimer.WaitTime = 1.0f / _fireRate;
            }
        }
    }

    [Export]
    protected Node3D BarrelEnd { get; set; } // Точка, откуда вылетают снаряды

    [ExportGroup("Audio")]
    [Export]
    protected AudioStream ShootSfx { get; set; }

    [Export]
    protected AudioStream ReloadSfx { get; set; }

    [Export]
    protected AudioStreamPlayer3D AudioPlayer { get; set; }


    private Timer _cooldownTimer;

    /// <summary>
    /// Проверяет, может ли турель произвести выстрел.
    /// </summary>
    public bool CanShoot => _cooldownTimer.IsStopped() && CurrentAmmo > 0 && Health > 0;

    public override void _Ready()
    {
        base._Ready();
        CurrentAmmo = MaxAmmo;

        // Настраиваем таймер для кулдауна
        _cooldownTimer = new Timer
        {
            WaitTime = 1.0f / FireRate,
            OneShot = true
        };
        AddChild(_cooldownTimer);

#if DEBUG
        if (ProjectileScene == null)
        {
            GD.PushError($"Для турели '{Name}' не указана сцена снаряда (ProjectileScene)!");
        }
        if (BarrelEnd == null)
        { 
            GD.PushWarning($"Для турели '{Name}' не указана точка вылета снаряда (BarrelEnd). Снаряды будут появляться в центре турели.");
        }
        if (AudioPlayer == null)
        {
            GD.PushWarning($"Для турели '{Name}' не указан AudioStreamPlayer3D. Звуки выстрелов и перезарядки не будут работать.");
        }
#endif
    }

    /// <summary>
    /// Производит выстрел, если это возможно.
    /// </summary>
    /// <returns>true, если выстрел был произведен, иначе false.</returns>
    public virtual bool Shoot()
    {
        if (!CanShoot)
        {
            return false;
        }

        CurrentAmmo--;
        _cooldownTimer.Start();

        var projectile = ProjectileScene.Instantiate<BaseProjectile>();
        var spawnPoint = BarrelEnd != null ? BarrelEnd.GlobalTransform : GlobalTransform;

        projectile.GlobalTransform = spawnPoint;

        Constants.Root.AddChild(projectile);

        if (ShootSfx != null && AudioPlayer != null)
        {
            AudioPlayer.Stream = ShootSfx;
            AudioPlayer.Play();
        }

        return true;
    }

    /// <summary>
    /// Перезаряжает турель.
    /// </summary>
    public virtual void Reload(int amount)
    {
        var oldAmmo = CurrentAmmo;
        CurrentAmmo = Mathf.Min(CurrentAmmo + amount, MaxAmmo);

        if (CurrentAmmo > oldAmmo && ReloadSfx != null && AudioPlayer != null)
        {
            AudioPlayer.Stream = ReloadSfx;
            AudioPlayer.Play();
        }
    }
}