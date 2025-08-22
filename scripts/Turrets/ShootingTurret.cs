using Godot;
using Game.Projectiles;
using System;
using Game.Interfaces;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для стреляющих турелей.
/// Расширяет BaseTurret, добавляя логику стрельбы, боеприпасов и перезарядки.
/// Определяет, *как* турель стреляет, но не *когда*.
/// Поддерживает несколько режимов стрельбы и автоматическую перезарядку.
/// </summary>
public abstract partial class ShootingTurret : BaseTurret, IShooter
{
    #region Enums & Events

    /// <summary>
    /// Определяет режим работы механизма стрельбы турели.
    /// </summary>
    public enum FiringMode
    {
        /// <summary>
        /// Стандартный режим: выстрел -> кулдаун -> выстрел.
        /// </summary>
        Standard,
        /// <summary>
        /// Режим с зарядкой: зарядка -> выстрел -> кулдаун.
        /// </summary>
        Charged
    }

    /// <summary>
    /// Состояния, в которых может находиться турель.
    /// </summary>
    public enum TurretState
    {
        Idle,           // Готова к действию
        Charging,       // Зарядка перед выстрелом (для Charged режима)
        FiringCooldown, // Между выстрелами
        Reloading,      // В процессе перезарядки
        Broken          // Уничтожена
    }

    /// <summary>
    /// Событие, вызываемое при смене состояния турели.
    /// </summary>
    public event Action<TurretState> OnStateChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Текущее состояние турели.
    /// </summary>
    public TurretState CurrentState { get; private set; } = TurretState.Idle;

    [ExportGroup("Shooting Mechanics")]
    /// <summary>
    /// Режим стрельбы турели
    /// </summary>
    [Export] public FiringMode Mode { get; private set; } = FiringMode.Standard;

    [Export(PropertyHint.File, "*.tscn,*.scn")]
    protected PackedScene ProjectileScene { get; private set; }

    [Export]
    public int MaxAmmo { get; private set; } = 100;

    [Export]
    public int CurrentAmmo { get; private set; }

    private float _fireRate = 1.0f;
    /// <summary>
    /// Скорострельность в выстрелах в секунду. Определяет кулдаун между выстрелами.
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

    [ExportGroup("Charged Shot Properties", "Charged")]
    /// <summary>
    /// Время в секундах, необходимое для зарядки выстрела в режиме 'Charged'.
    /// </summary>
    [Export] public float ChargeTime { get; private set; } = 0.5f;

    [ExportGroup("Reloading")]
    [Export] private Timer _reloadTimer;
    /// <summary>
    /// Если true, турель автоматически начнет перезарядку после выстрела последним патроном.
    /// </summary>
    [Export] public bool AutoReload { get; private set; } = true;

    [ExportGroup("Audio")]
    [Export] protected AudioStream ShootSfx { get; private set; }
    [Export] protected AudioStream ReloadSfx { get; private set; }
    /// <summary>
    /// Звук, проигрываемый во время зарядки выстрела в режиме 'Charged'.
    /// </summary>
    [Export] protected AudioStream ChargeSfx { get; private set; }

    [Export] protected AudioStreamPlayer3D ShootAudioPlayer { get; private set; }
    [Export] protected AudioStreamPlayer3D UtilityAudioPlayer { get; private set; }

    /// <summary>
    /// Проверяет, есть ли патроны в турели.
    /// </summary>
    public bool HasAmmo => CurrentAmmo > 0;

#if DEBUG
    /// <summary>
    /// Проверяет, может ли турель инициировать выстрел в данный момент.
    /// </summary>
    public bool CanShoot
    {
        get
        {
            if (CurrentState == TurretState.Idle && HasAmmo && IsAlive)
            {
                return true;
            }
            GD.Print($"[{Name}] Cant shoot: State={CurrentState}, HasAmmo={HasAmmo}, IsAlive={IsAlive}");
            return false;
        }
    }
#else
    public bool CanShoot => CurrentState == TurretState.Idle && HasAmmo && IsAlive;
#endif

    #endregion

    #region Private Fields
    private Timer _cooldownTimer;
    private Timer _chargeTimer;
    #endregion

    #region Godot Lifecycle
    public override void _EnterTree()
    {
        base._EnterTree();

        // Таймер кулдауна между выстрелами
        _cooldownTimer = new()
        {
            Name = "CooldownTimer",
            WaitTime = 1.0f / FireRate,
            OneShot = true
        };
        _cooldownTimer.Timeout += OnCooldownFinished;
        AddChild(_cooldownTimer);

        if (ChargeTime > 0)
        {
            // Таймер для зарядки выстрела
            _chargeTimer = new()
            {
                Name = "ChargeTimer",
                WaitTime = ChargeTime,
                OneShot = true
            };
            _chargeTimer.Timeout += OnChargeFinished;
            AddChild(_chargeTimer);
        }

        // Настраиваем таймер перезарядки
        if (_reloadTimer != null)
        {
            GD.Print($"Турель '{Name}' таймер перезарядки: {_reloadTimer.WaitTime} сек.");
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
        if (ShootAudioPlayer == null) GD.PushWarning($"Для турели '{Name}' не указан AudioStreamPlayer3D для стрельбы.");
        if (UtilityAudioPlayer == null) GD.PushWarning($"Для турели '{Name}' не указан UtilityAudioPlayer3D для доп. звуков.");
#endif
    }
    #endregion

    #region Shooting Logic

    /// <summary>
    /// Инициирует процесс выстрела в зависимости от текущего режима <see cref="FiringMode"/>.
    /// </summary>
    /// <returns>true, если процесс выстрела был успешно начат, иначе false.</returns>
    public virtual bool Shoot()
    {
        if (!CanShoot)
        {
            return false;
        }

        return Mode switch
        {
            FiringMode.Standard => StandardShoot(),
            FiringMode.Charged => ChargeAndShoot(),
            _ => throw new ArgumentOutOfRangeException(nameof(Mode), $"Неизвестный режим стрельбы: {Mode}")
        };
    }

    /// <summary>
    /// Выполняет логику стандартного выстрела.
    /// </summary>
    private bool StandardShoot()
    {
        SetState(TurretState.FiringCooldown);
        _cooldownTimer.Start();
        PerformShot();
        return true;
    }

    /// <summary>
    /// Начинает процесс зарядки для последующего выстрела.
    /// </summary>
    private bool ChargeAndShoot()
    {
        SetState(TurretState.Charging);
        _chargeTimer.Start();
        PlaySound(ChargeSfx, UtilityAudioPlayer);
        return true;
    }

    /// <summary>
    /// Общая логика для совершения выстрела: создание снаряда, расход патрона и проигрывание звука.
    /// Также проверяет необходимость авто-перезарядки.
    /// </summary>
    private void PerformShot()
    {
        CurrentAmmo--;

        // NOTE: Используем GlobalTransform точки вылета, если она есть, иначе - самой турели.
        var spawnPoint = BarrelEnd != null ? BarrelEnd.GlobalTransform : GlobalTransform;
        var projectile = CreateProjectile(spawnPoint);

        PlaySound(ShootSfx, ShootAudioPlayer);

        GD.Print($"[{Name}] Выстрел с {projectile.Name} в {spawnPoint.Origin}!");
    }

    /// <summary>
    /// Создает (или получает из пула) экземпляр снаряда в указанной точке.
    /// </summary>
    public virtual BaseProjectile CreateProjectile(Transform3D spawnPoint)
    {
        // Вместо Instantiate используем наш пул!
        var projectile = ProjectilePool.Get(ProjectileScene);

        projectile.GlobalTransform = spawnPoint;

        // Добавляем снаряд в корень сцены, чтобы он не зависел от турели.
        Constants.Root.AddChild(projectile);

        // Инициализируем снаряд после того, как он добавлен в сцену и его позиция установлена
        projectile.Initialize(this);
        return projectile;
    }

    #endregion

    #region Reloading Logic

    /// <summary>
    /// Начинает процесс перезарядки турели.
    /// </summary>
    public virtual bool StartReload()
    {
        // Нельзя перезаряжаться, если мы уже что-то делаем или магазин полон.
        if (CurrentState != TurretState.Idle || CurrentAmmo == MaxAmmo)
        {
            GD.Print($"[{Name}] Невозможно начать перезарядку. Состояние: {CurrentState}, Патроны: {CurrentAmmo}/{MaxAmmo}");
            return false;
        }

        SetState(TurretState.Reloading);
        _reloadTimer.Start();
        PlaySound(ReloadSfx, UtilityAudioPlayer);
        GD.Print($"[{Name}] Перезарядка началась...");
        return true;
    }
    #endregion

    #region State Management & Overrides

    public override bool Destroy()
    {
        SetState(TurretState.Broken);
        // Останавливаем все таймеры, чтобы избежать вызовов по таймауту на удаленном объекте
        _cooldownTimer.Stop();
        _reloadTimer?.Stop();
        _chargeTimer.Stop();

        return base.Destroy();
    }

    public Node3D GetShootInitiator() => this;

    /// <summary>
    /// Безопасно изменяет состояние турели и вызывает событие <see cref="OnStateChanged"/>.
    /// </summary>
    /// <param name="newState">Новое состояние.</param>
    private void SetState(TurretState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        OnStateChanged?.Invoke(CurrentState);
    }

    #endregion

    #region Signal Handlers

    /// <summary>
    /// Вызывается по окончании таймера скорострельности.
    /// </summary>
    private void OnCooldownFinished()
    {
        // Если мы не в другом состоянии (например, не начали перезарядку), возвращаемся в Idle.
        if (CurrentState == TurretState.FiringCooldown)
        {
            SetState(TurretState.Idle);

            // Автоматическая перезарядка, если есть патроны.
            if (AutoReload)
            {
                GD.Print($"[{Name}] Автоматическая перезарядка началась.");
                StartReload();
            }
        }
    }

    /// <summary>
    /// Вызывается по окончании таймера зарядки (для режима Charged).
    /// </summary>
    private void OnChargeFinished()
    {
        // Убеждаемся, что турель не была уничтожена или не начала перезарядку во время зарядки.
        if (CurrentState != TurretState.Charging) return;

        // Переходим в кулдаун и запускаем его таймер *перед* выстрелом.
        SetState(TurretState.FiringCooldown);
        _cooldownTimer.Start();
        PerformShot();
    }

    /// <summary>
    /// Вызывается по окончании таймера перезарядки.
    /// </summary>
    private void OnReloadFinished()
    {
        if (CurrentState == TurretState.Reloading)
        {
            CurrentAmmo = MaxAmmo; // Пополняем патроны В КОНЦЕ
            SetState(TurretState.Idle);
            GD.Print($"[{Name}] Перезарядка завершена!");
        }
    }

    #endregion

    #region Helpers
    /// <summary>
    /// Вспомогательный метод для проигрывания звука.
    /// </summary>
    private static void PlaySound(AudioStream sound, AudioStreamPlayer3D player)
    {
        if (sound != null && player != null)
        {
            player.Stream = sound;
            player.Play();
        }
    }
    #endregion
}