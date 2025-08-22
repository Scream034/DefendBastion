using Godot;
using Game.Projectiles;
using System;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для стреляющих турелей.
/// Расширяет BaseTurret, добавляя логику стрельбы, боеприпасов и перезарядки.
/// Определяет, *как* турель стреляет, но не *когда*.
/// </summary>
public abstract partial class ShootingTurret : BaseTurret
{
    public enum TurretState
    {
        Idle, // Готова к стрельбе
        FiringCooldown, // Между выстрелами
        Reloading, // В процессе перезарядки
        Broken // Уничтожена
    }

    public event Action<TurretState> OnStateChanged;

    public TurretState CurrentState { get; private set; } = TurretState.Idle;

    [ExportGroup("Shooting Mechanics")]
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    protected PackedScene ProjectileScene { get; private set; }

    [Export]
    public int MaxAmmo { get; private set; } = 100;

    [Export]
    public int CurrentAmmo { get; private set; }

    [Export(PropertyHint.Range, "0.5, 10.0, 0.1")]
    public float ReloadTime { get; private set; } = 2.0f;

    private float _fireRate = 1.0f;
    /// <summary>
    /// Скорострельность в выстрелах в секунду.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float FireRate
    {
        get => _fireRate;
        set
        {
            _fireRate = Mathf.Max(0.1f, value); // Защита от нулевого или отрицательного значения
            if (_cooldownTimer != null)
            {
                _cooldownTimer.WaitTime = 1.0f / _fireRate;
            }
        }
    }

    /// <summary>
    /// Точка, откуда вылетают снаряды. Обычно это Marker3D.
    /// </summary>
    [Export]
    protected Node3D BarrelEnd { get; private set; }

    private Timer _cooldownTimer;

    [Export]
    private Timer _reloadTimer;

    [ExportGroup("Audio")]
    [Export] protected AudioStream ShootSfx { get; private set; }
    [Export] protected AudioStream ReloadSfx { get; private set; }

    [Export] protected AudioStreamPlayer3D ShootAudioPlayer { get; private set; }
    [Export] protected AudioStreamPlayer3D UtilityAudioPlayer { get; private set; }

    /// <summary>
    /// Проверяет, есть ли патроны в турели.
    /// </summary>
    public bool HasAmmo => CurrentAmmo > 0;

    /// <summary>
    /// Проверяет, может ли турель произвести выстрел в данный момент.
    /// </summary>
    public bool CanShoot => CurrentState == TurretState.Idle && HasAmmo && IsAlive;

    public override void _EnterTree()
    {
        base._EnterTree();

        _cooldownTimer = new()
        {
            WaitTime = 1.0f / FireRate,
            OneShot = true
        };
        _cooldownTimer.Timeout += OnCooldownFinished;
        AddChild(_cooldownTimer);

        // Настраиваем таймер перезарядки
        if (_reloadTimer != null)
        {
            _reloadTimer.WaitTime = ReloadTime;
            _reloadTimer.OneShot = true;
            _reloadTimer.Timeout += OnReloadFinished;
        }
    }

    public override void _Ready()
    {
        base._Ready();
        CurrentAmmo = MaxAmmo;

#if DEBUG
        // Улучшенные проверки на null
        if (ProjectileScene == null) GD.PushError($"Для турели '{Name}' не указана сцена снаряда (ProjectileScene)!");
        if (BarrelEnd == null) GD.PushWarning($"Для турели '{Name}' не указана точка вылета снаряда (BarrelEnd). Снаряды будут появляться в центре турели.");
        if (ShootAudioPlayer == null) GD.PushWarning($"Для турели '{Name}' не указан AudioStreamPlayer3D. Звуки не будут работать.");
        if (_cooldownTimer == null) GD.PushError($"Для турели '{Name}' не назначен узел Timer в поле Cooldown Timer. Стрельба не будет работать.");
#endif
    }

    /// <summary>
    /// Производит выстрел, если это возможно. Создает снаряд, тратит патрон и запускает кулдаун.
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
        CurrentState = TurretState.FiringCooldown;
        OnStateChanged?.Invoke(CurrentState);

        var projectile = ProjectileScene.Instantiate<BaseProjectile>();

        // NOTE: Используем GlobalTransform точки вылета, если она есть, иначе - самой турели.
        var spawnPoint = BarrelEnd != null ? BarrelEnd.GlobalTransform : GlobalTransform;
        projectile.GlobalTransform = spawnPoint;

        // NOTE: Добавляем снаряд в корень сцены, чтобы он не зависел от турели
        // (например, чтобы снаряд не исчез, если турель уничтожат в полете).
        Constants.Root.AddChild(projectile);

        PlaySound(ShootSfx, ShootAudioPlayer);

        return true;
    }

    /// <summary>
    /// Перезаряжает турель, добавляя указанное количество патронов.
    /// </summary>
    public virtual bool StartReload()
    {
        // Нельзя перезаряжаться, если мы уже что-то делаем или магазин полон.
        if (CurrentState != TurretState.Idle || CurrentAmmo == MaxAmmo)
        {
            return false;
        }

        CurrentState = TurretState.Reloading;
        OnStateChanged?.Invoke(CurrentState);

        _reloadTimer.Start();

        PlaySound(ReloadSfx, UtilityAudioPlayer);
        GD.Print("Reloading started...");
        return true;
    }

    /// <summary>
    /// Вспомогательный метод для проигрывания звука.
    /// </summary>
    private static void PlaySound(AudioStream sound, AudioStreamPlayer3D player)
    {
        if (sound != null && player != null)
        {
            // Не прерываем уже играющий звук на этом плеере, если он важен
            // Хотя для выстрелов это обычно не нужно, а для перезарядки - полезно
            if (player.Playing) return;

            player.Stream = sound;
            player.Play();
        }
    }

    /// <summary>
    /// Вызывается по окончании таймера скорострельности
    /// </summary>
    private void OnCooldownFinished()
    {
        // Если мы не в другом состоянии (например, нас не уничтожили), возвращаемся в Idle
        if (CurrentState == TurretState.FiringCooldown)
        {
            CurrentState = TurretState.Idle;
            OnStateChanged?.Invoke(CurrentState);
        }
    }

    /// <summary>
    /// Вызывается по окончании таймера перезарядки
    /// </summary>
    private void OnReloadFinished()
    {
        if (CurrentState == TurretState.Reloading)
        {
            CurrentAmmo = MaxAmmo; // Пополняем патроны В КОНЦЕ
            CurrentState = TurretState.Idle;
            OnStateChanged?.Invoke(CurrentState);
            GD.Print("Reloading finished!");
        }
    }

    public override bool Destroy()
    {
        CurrentState = TurretState.Broken;
        return base.Destroy();
    }
}