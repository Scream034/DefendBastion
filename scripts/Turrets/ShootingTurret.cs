using Godot;
using Game.Projectiles;
using System;
using Game.Interfaces;
using System.Threading.Tasks;
using Game.Singletons;
using Game.Entity;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для стреляющих турелей.
/// Расширяет BaseTurret, добавляя логику стрельбы, боеприпасов и перезарядки.
/// Определяет, *как* турель стреляет, но не *когда*.
/// Поддерживает несколько режимов стрельбы и автоматическую перезарядку.
/// </summary>
public abstract partial class ShootingTurret : BaseTurret, IShooter
{
    #region Classes

    /// <summary>
    /// Состояния, в которых может находиться турель.
    /// </summary>
    public enum TurretState
    {
        Idle,           // Готова к действию
        FiringCooldown, // Между выстрелами
        Reloading,      // В процессе перезарядки
        Broken          // Уничтожена
    }

    public static class Animations
    {
        public const string Fire = "Fire";
        public const string Reload = "Reload";
        public const string Break = "Break";
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
    public TurretState CurrentState { get; protected set; } = TurretState.Idle;

    [ExportGroup("Shooting Mechanics")]
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    public PackedScene ProjectileScene { get; private set; }

    [ExportGroup("Shooting Effects")]
    /// <summary>
    /// Сила тряски камеры при выстреле
    /// </summary>
    [Export(PropertyHint.Range, "0, 100, 0.1")] private float _shotShakeStrength = 1f;

    /// <summary>
    /// Радиус действия тряски в метрах (тряска ощущается только в пределах этого радиуса)
    /// </summary>
    [Export(PropertyHint.Range, "1, 100, 1")] private float _shotShakeRadius = 50f;

    /// <summary>
    /// Длительность тряски в секундах
    /// </summary>
    [Export(PropertyHint.Range, "0.05, 1.0, 0.05")] private float _shotShakeDuration = 0.2f;

    [Export(PropertyHint.Range, "-1,10000,1")]
    public int MaxAmmo { get; protected set; } = 100;

    [Export(PropertyHint.Range, "-1,10000,1")]
    public int CurrentAmmo { get; protected set; }

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
    /// Точка, откуда вылетают снаряды.
    /// </summary>
    [Export]
    public Marker3D BarrelEnd { get; private set; }

    [ExportGroup("Reloading")]
    /// <summary>
    /// Если true, турель автоматически начнет перезарядку после выстрела последним патроном.
    /// </summary>
    [Export] public bool AutoReload { get; private set; } = true;

    [Export] protected AnimationPlayer AnimationPlayer { get; private set; }

    /// <summary>
    /// Проверяет, есть ли патроны в турели.
    /// </summary>
    public bool HasAmmo => CurrentAmmo > 0;

    /// <summary>
    /// Проверяет, бесконечное ли количество патронов в турели.
    /// </summary>
    public bool HasInfiniteAmmo => MaxAmmo < 0;

    /// <summary>
    /// Проверяет, полный ли магазин в турели.
    /// </summary>
    public bool HasFullAmmo => CurrentAmmo == MaxAmmo;

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
            GD.Print($"{Name} Cant shoot: State={CurrentState}, HasAmmo={HasAmmo}, IsAlive={IsAlive}");
            return false;
        }
    }
#else
    public bool CanShoot => CurrentState == TurretState.Idle && HasAmmo && IsAlive;
#endif

    #endregion

    #region Public Getters
    public Node3D GetShootInitiator() => this;
    #endregion

    private Timer _cooldownTimer;

    #region Godot
    public override void _EnterTree()
    {
        base._EnterTree();
#if DEBUG
        // Улучшенные проверки на null
        if (ProjectileScene == null) GD.PushError($"Для турели '{Name}' не указана сцена снаряда (ProjectileScene)!");
        if (BarrelEnd == null) GD.PushWarning($"Для турели '{Name}' не указана точка вылета снаряда (BarrelEnd). Снаряды будут появляться в центре турели.");
#endif

        // Таймер кулдауна между выстрелами
        _cooldownTimer = new Timer
        {
            Name = "CooldownTimer",
            WaitTime = 1.0f / FireRate,
            OneShot = true
        };
        AddChild(_cooldownTimer);
        _cooldownTimer.Timeout += OnCooldownFinished;

        AnimationPlayer.AnimationFinished += OnAnimationChanged;
    }

    public override void _Ready()
    {
        base._Ready();
        CurrentAmmo = HasInfiniteAmmo ? 1 : MaxAmmo;
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

        SetState(TurretState.FiringCooldown);
        _cooldownTimer?.Start();
        AnimationPlayer.Play(Animations.Fire);

        return true;
    }

    /// <summary>
    /// Вызывает эффект тряски камеры. Может быть вызван из AnimationPlayer.
    /// </summary>
    public void TriggerShotShake()
    {
        GlobalEvents.Instance.RequestWorldShake(BarrelEnd.GlobalPosition, _shotShakeStrength, _shotShakeRadius, _shotShakeDuration);
    }

    /// <summary>
    /// Общая логика для совершения выстрела: создание снаряда, расход патрона и проигрывание звука.
    /// Также проверяет необходимость авто-перезарядки.
    /// </summary>
    private void PerformShotLogic()
    {
        ConsumeAmmo();

        // NOTE: Используем GlobalTransform точки вылета, если она есть, иначе - самой турели.
        var spawnPoint = BarrelEnd != null ? BarrelEnd.GlobalTransform : GlobalTransform;
        var projectile = CreateProjectile(spawnPoint);
        GD.Print($"{Name} Shooting with {projectile.Name} at: {BarrelEnd.Position}");
    }

    /// <summary>
    /// Создает (или получает из пула) экземпляр снаряда в указанной точке.
    /// </summary>
    public virtual BaseProjectile CreateProjectile(Transform3D spawnPoint, LivingEntity initiator = null)
    {
        // Вместо Instantiate используем наш пул!
        var projectile = ProjectilePool.Get(ProjectileScene);

        projectile.GlobalTransform = spawnPoint;

        // Добавляем снаряд в корень сцены, чтобы он не зависел от турели.
        Constants.Root.AddChild(projectile);

        // Инициализируем снаряд после того, как он добавлен в сцену и его позиция установлена
        projectile.Initialize(initiator);
        return projectile;
    }

    /// <summary>
    /// Вычитает патроны из турели.
    /// </summary>
    /// <returns></returns>
    public virtual bool ConsumeAmmo(int amount = 1)
    {
        if (HasInfiniteAmmo) return false;
        CurrentAmmo = Mathf.Max(0, CurrentAmmo - amount);
        return true;
    }

    #endregion

    #region Reloading Logic

    /// <summary>
    /// Начинает процесс перезарядки турели.
    /// </summary>
    public virtual bool StartReload()
    {
        // Нельзя перезаряжаться, если мы уже что-то делаем или магазин полон.
        if (CurrentState != TurretState.Idle || HasFullAmmo || HasInfiniteAmmo)
        {
            GD.Print($"{Name} Невозможно начать перезарядку. Состояние: {CurrentState}, Патроны: {CurrentAmmo}/{MaxAmmo} (INF:{HasInfiniteAmmo})");
            return false;
        }

        SetState(TurretState.Reloading);
        AnimationPlayer.Play(Animations.Reload);
        GD.Print($"{Name} Перезарядка началась...");
        return true;
    }
    #endregion

    #region State Management & Overrides

    public override async Task<bool> DestroyAsync()
    {
        if (IsQueuedForDeletion()) return false;

        // Выполняем специфичную для ShootingTurret логику:
        SetState(TurretState.Broken);
        _cooldownTimer.Stop(); // Останавливаем все таймеры
        AnimationPlayer.Play(Animations.Break);

        // Ждем, пока анимация разрушения ЗАВЕРШИТСЯ.
        //    ВАЖНО: Использовать сигнал AnimationFinished, а не CurrentAnimationChanged.
        //    CurrentAnimationChanged сработает в момент НАЧАЛА новой анимации, а не ее конца.
        await ToSignal(AnimationPlayer, AnimationPlayer.SignalName.AnimationFinished);

        // Теперь, когда анимация проиграна, вызываем метод базового класса,
        //    чтобы он выполнил свою часть работы (вызвал OnDestroyed и удалил объект).
        //    Ключевой момент - `await base.DestroyAsync()`.
        return await base.DestroyAsync();
    }

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

    /// <summary>
    /// Установливает турель в режим Idle. (Используется для анимаций)
    /// </summary>
    private void IdleState()
    {
        SetState(TurretState.Idle);
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

            // Автоматическая перезарядка, если закончились патроны
            if (AutoReload && !HasAmmo && !HasInfiniteAmmo)
            {
                GD.Print($"{Name} Автоматическая перезарядка началась, т.к. закончились патроны.");
                StartReload();
            }
        }
    }

    private void OnAnimationChanged(StringName animationName)
    {
#if DEBUG
        GD.Print($"{Name} Анимация закончилась: {animationName}");
#endif
        if (CurrentState == TurretState.Reloading && animationName == Animations.Reload)
        {
            OnReloadFinished();
        }
    }

    /// <summary>
    /// Вызывается при окончании анимации перезарядки.
    /// </summary>
    private void OnReloadFinished()
    {
        CurrentAmmo = MaxAmmo; // Пополняем патроны В КОНЦЕ
        SetState(TurretState.Idle);
        GD.Print($"{Name} Перезарядка завершена!");
    }

    #endregion
}