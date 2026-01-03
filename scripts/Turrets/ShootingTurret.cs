#nullable enable

using Godot;
using Game.Projectiles;
using System;
using Game.Interfaces;
using System.Threading.Tasks;
using Game.Singletons;
using Game.Entity;

namespace Game.Turrets;

/// <summary>
/// Базовый класс для стреляющих турелей с поддержкой разных режимов стрельбы,
/// магазинной системы и состояний (State Machine).
/// </summary>
public partial class ShootingTurret : BaseTurret, IShooter
{
    #region Enums & Events

    /// <summary>
    /// Возможные состояния турели.
    /// </summary>
    public enum TurretState
    {
        Idle,           // Готова к действию
        Shooting,       // В процессе анимации выстрела
        FiringCooldown, // Техническая пауза между выстрелами (Cycle Time)
        Reloading,      // Процесс перезарядки магазина
        Broken          // Турель уничтожена
    }

    /// <summary>
    /// Режим ведения огня.
    /// </summary>
    public enum FireMode
    {
        /// <summary>
        /// Один выстрел -> Требуется полная перезарядка (пример: Танковое орудие).
        /// </summary>
        Single,

        /// <summary>
        /// Один клик -> Один выстрел. Перезарядка только когда пуст магазин (пример: Полуавтоматическая пушка).
        /// </summary>
        SemiAuto,

        /// <summary>
        /// Стрельба очередью пока зажата кнопка (пример: Пулемет).
        /// </summary>
        FullAuto
    }

    public static class Animations
    {
        public const string Fire = "Fire";
        public const string Reload = "Reload";
        public const string Break = "Break";
    }

    /// <summary>
    /// Вызывается при изменении состояния (например, для обновления UI прицела).
    /// </summary>
    public event Action<TurretState>? OnStateChanged;

    /// <summary>
    /// Вызывается при изменении количества патронов (для UI).
    /// </summary>
    public event Action? OnAmmoChanged;

    /// <summary>
    /// Вызывается в момент фактического выстрела (снаряд вылетел).
    /// </summary>
    public event Action? OnShot;

    #endregion

    #region Export Properties

    [ExportGroup("Firing Settings")]
    [Export] public FireMode CurrentFireMode { get; private set; } = FireMode.Single;

    /// <summary>
    /// Скорострельность (выстрелов в секунду).
    /// Влияет на паузу между выстрелами в режиме Auto/SemiAuto.
    /// </summary>
    [Export(PropertyHint.Range, "0.1, 60.0, 0.1")]
    public float FireRate { get; set; } = 1.0f;

    [ExportGroup("Ammo & Magazine")]
    /// <summary>
    /// Размер магазина (количество выстрелов до перезарядки).
    /// Для режима Single должно быть равно 1.
    /// </summary>
    [Export(PropertyHint.Range, "1, 1000, 1")]
    public int MagazineSize { get; private set; } = 1;

    /// <summary>
    /// Максимальный общий запас патронов (в резерве + в магазине).
    /// -1 для бесконечного боезапаса.
    /// </summary>
    [Export(PropertyHint.Range, "-1, 10000, 1")]
    public int MaxAmmoCapacity { get; protected set; } = 100;

    /// <summary>
    /// Текущие патроны в магазине (готовые к стрельбе).
    /// </summary>
    [Export(PropertyHint.Range, "0, 1000, 1")]
    public int CurrentAmmo { get; protected set; }

    /// <summary>
    /// Патроны в запасе (из которых наполняется магазин).
    /// </summary>
    [Export(PropertyHint.Range, "0, 10000, 1")]
    public int ReserveAmmo { get; protected set; }

    [ExportGroup("Components")]
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    public PackedScene? ProjectileScene { get; private set; }

    [Export] public Marker3D? BarrelEnd { get; private set; }

    [Export] protected AnimationPlayer? AnimationPlayer { get; private set; }

    [ExportGroup("Shooting Effects")]
    [Export(PropertyHint.Range, "0, 100, 0.1")] private float _shotShakeStrength = 1f;
    [Export(PropertyHint.Range, "1, 100, 1")] private float _shotShakeRadius = 50f;
    [Export(PropertyHint.Range, "0.05, 1.0, 0.05")] private float _shotShakeDuration = 0.2f;

    [ExportGroup("Reloading Logic")]
    /// <summary>
    /// Автоматически начинать перезарядку, если магазин пуст.
    /// </summary>
    [Export] public bool AutoReload { get; private set; } = true;

    #endregion

    #region Runtime State

    public TurretState CurrentState { get; protected set; } = TurretState.Idle;
    private Timer _cooldownTimer = null!;

    // Публичные геттеры для проверки состояния
    public bool HasInfiniteAmmo => MaxAmmoCapacity < 0;

    // Есть ли патроны прямо сейчас в стволе
    public bool HasAmmoInMag => CurrentAmmo > 0 || HasInfiniteAmmo;

    // Полон ли магазин
    public bool HasFullMagazine => CurrentAmmo >= MagazineSize;

    // Есть ли чем перезарядиться
    public bool HasReserveAmmo => ReserveAmmo > 0 || HasInfiniteAmmo;

    // Общее количество (для UI)
    public int TotalAmmoCount => HasInfiniteAmmo ? 999 : (CurrentAmmo + ReserveAmmo);

    /// <summary>
    /// Может ли турель произвести выстрел прямо сейчас.
    /// </summary>
    public bool CanShoot => CurrentState == TurretState.Idle && HasAmmoInMag && IsAlive;

    /// <summary>
    /// Можно ли начать перезарядку.
    /// </summary>
    public bool CanReload => CurrentState == TurretState.Idle
                             && !HasFullMagazine
                             && HasReserveAmmo
                             && IsAlive;

    #endregion

    #region Godot Lifecycle

    public override void _EnterTree()
    {
        base._EnterTree();

        // Создаем таймер для контроля скорострельности (Cooldown)
        _cooldownTimer = new Timer
        {
            Name = "FireRateTimer",
            OneShot = true,
            ProcessCallback = Timer.TimerProcessCallback.Physics
        };
        AddChild(_cooldownTimer);
        _cooldownTimer.Timeout += OnCooldownFinished;

        if (AnimationPlayer != null)
        {
            AnimationPlayer.AnimationFinished += OnAnimationFinished;
        }

#if DEBUG
        if (ProjectileScene == null) GD.PushError($"[{Name}] ProjectileScene не назначена!");
        if (BarrelEnd == null) GD.PushWarning($"[{Name}] BarrelEnd не назначен. Стрельба будет вестись из центра.");
#endif
    }

    public override void _Ready()
    {
        base._Ready();
        InitializeAmmo();
    }

    /// <summary>
    /// Инициализация боезапаса при старте.
    /// </summary>
    private void InitializeAmmo()
    {
        if (HasInfiniteAmmo)
        {
            CurrentAmmo = MagazineSize;
            ReserveAmmo = 0;
        }
        else
        {
            // Симулируем "полную загрузку" при спавне
            // Сначала заполняем магазин
            CurrentAmmo = Mathf.Min(MagazineSize, MaxAmmoCapacity);
            // Остальное в резерв
            ReserveAmmo = Mathf.Max(0, MaxAmmoCapacity - CurrentAmmo);
        }

        // Устанавливаем время таймера на основе скорострельности
        _cooldownTimer.WaitTime = 1.0f / FireRate;
    }

    #endregion

    #region Shooting Logic

    /// <summary>
    /// Попытка произвести выстрел.
    /// </summary>
    /// <returns>True, если выстрел (или анимация выстрела) началась.</returns>
    public virtual bool Shoot()
    {
        if (!CanShoot) return false;

        SetState(TurretState.Shooting);

        if (AnimationPlayer != null && AnimationPlayer.HasAnimation(Animations.Fire))
        {
            // Если есть анимация, полагаемся на неё.
            // В самой анимации должен быть Call Method Track, вызывающий PerformShot().
            // Или, если анимация простая, PerformShot вызовется по окончании (см. OnAnimationFinished) - 
            // но лучше вызывать явно в начале анимации для отзывчивости.
            AnimationPlayer.Play(Animations.Fire);

            // Фолбэк: если в анимации нет вызова PerformShot, можно раскомментировать вызов ниже,
            // но это может привести к двойным выстрелам, если трек есть. 
            // Для надежности здесь полагаемся, что метод будет вызван через AnimationPlayer.
            // Если анимации нет вообще - вызываем вручную.
        }
        else
        {
            // Если анимации нет, стреляем сразу
            PerformShot();
            // Имитируем окончание анимации для перехода в кулдаун
            OnShootSequenceFinished();
        }

        return true;
    }

    /// <summary>
    /// Фактическая логика выстрела: трата патрона, спавн снаряда, эффекты.
    /// Этот метод должен вызываться AnimationPlayer'ом (Call Method Track) или вручную.
    /// </summary>
    public void PerformShot()
    {
        // Дополнительная проверка, чтобы не стрелять дважды за одну анимацию (если настроено криво)
        if (CurrentState != TurretState.Shooting) return;

        ConsumeAmmoInMag();
        SpawnProjectile();
        TriggerShotShake();

        OnShot?.Invoke();
    }

    private void SpawnProjectile()
    {
        if (ProjectileScene == null) return;

        var spawnTrans = BarrelEnd != null ? BarrelEnd.GlobalTransform : GlobalTransform;
        CreateProjectile(spawnTrans, this);
    }

    public virtual BaseProjectile CreateProjectile(Transform3D spawnPoint, LivingEntity initiator)
    {
        // Используем пул объектов для производительности
        var projectile = ProjectilePool.Instance.Get(ProjectileScene!);

        projectile.GlobalTransform = spawnPoint;
        Constants.Root.AddChild(projectile);

        projectile.Initialize(initiator);

#if DEBUG
        GD.Print($"[{Name}] Projectile Spawned: {projectile.Name}");
#endif
        return projectile;
    }

    private void ConsumeAmmoInMag()
    {
        if (HasInfiniteAmmo) return;

        CurrentAmmo = Mathf.Max(0, CurrentAmmo - 1);
        OnAmmoChanged?.Invoke();
    }

    /// <summary>
    /// Эффект тряски камеры.
    /// </summary>
    public void TriggerShotShake()
    {
        GlobalEvents.Instance.RequestWorldShake(
            BarrelEnd?.GlobalPosition ?? GlobalPosition,
            _shotShakeStrength,
            _shotShakeRadius,
            _shotShakeDuration
        );
    }

    #endregion

    #region Reloading Logic

    public virtual bool StartReload()
    {
        if (!CanReload) return false;

        SetState(TurretState.Reloading);

        if (AnimationPlayer != null && AnimationPlayer.HasAnimation(Animations.Reload))
        {
            AnimationPlayer.Play(Animations.Reload);
        }
        else
        {
            // Если анимации нет, перезаряжаемся мгновенно (или добавить таймер)
            FinishReload();
        }

        return true;
    }

    /// <summary>
    /// Завершает перезарядку, пересчитывает патроны.
    /// Вызывается по окончании анимации перезарядки.
    /// </summary>
    public void FinishReload()
    {
        if (CurrentState != TurretState.Reloading) return;

        int needed = MagazineSize - CurrentAmmo;

        // Сколько реально можем зарядить
        int toLoad = HasInfiniteAmmo ? needed : Mathf.Min(needed, ReserveAmmo);

        CurrentAmmo += toLoad;
        if (!HasInfiniteAmmo)
        {
            ReserveAmmo -= toLoad;
        }

        OnAmmoChanged?.Invoke();

        // Возвращаемся в строй
        SetState(TurretState.Idle);

#if DEBUG
        GD.Print($"[{Name}] Reloaded. Mag: {CurrentAmmo}, Reserve: {ReserveAmmo}");
#endif
    }

    #endregion

    #region State Machine & Event Handlers

    private void SetState(TurretState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        OnStateChanged?.Invoke(CurrentState);
    }

    /// <summary>
    /// Обработчик окончания любой анимации.
    /// </summary>
    private void OnAnimationFinished(StringName animName)
    {
        if (animName == Animations.Fire)
        {
            OnShootSequenceFinished();
        }
        else if (animName == Animations.Reload)
        {
            FinishReload();
        }
    }

    /// <summary>
    /// Логика после завершения цикла выстрела (анимации).
    /// Решает, переходить ли в Idle, Cooldown или Reload.
    /// </summary>
    private void OnShootSequenceFinished()
    {
        // Если это однозарядное орудие (Танк)
        if (CurrentFireMode == FireMode.Single)
        {
            // Сразу пытаемся перезарядиться, если включена авто-перезарядка
            if (AutoReload && HasReserveAmmo)
            {
                SetState(TurretState.Idle);
                StartReload();
            }
        }
        // Если магазинное орудие (Пулемет, Автопушка)
        else
        {
            if (HasAmmoInMag)
            {
                // Если есть патроны, уходим в кулдаун перед следующим выстрелом
                SetState(TurretState.FiringCooldown);
                _cooldownTimer.Start(); // WaitTime задан в _Ready на основе FireRate
            }
            else if (AutoReload && HasReserveAmmo)
            {
                // Магазин пуст, авто-перезарядка
                SetState(TurretState.Idle);
                StartReload();
            }
        }
    }

    /// <summary>
    /// Обработчик таймера скорострельности.
    /// </summary>
    private void OnCooldownFinished()
    {
        if (CurrentState != TurretState.FiringCooldown) return;

        // Кулдаун прошел, готовы стрелять снова
        SetState(TurretState.Idle);

        // Если это FullAuto и кнопка всё еще зажата, контроллер (Player/AI) 
        // вызовет Shoot() снова в своем _Process, так как IsIdle теперь true.
    }

    public override async Task<bool> DestroyAsync()
    {
        if (IsQueuedForDeletion()) return false;

        SetState(TurretState.Broken);
        _cooldownTimer?.Stop();

        if (AnimationPlayer != null && AnimationPlayer.HasAnimation(Animations.Break))
        {
            AnimationPlayer.Play(Animations.Break);
            await ToSignal(AnimationPlayer, AnimationPlayer.SignalName.AnimationFinished);
        }

        return await base.DestroyAsync();
    }

    #endregion

    #region External API

    public Node3D GetShootInitiator() => this;

    /// <summary>
    /// Добавляет патроны в резерв (подбор боеприпасов).
    /// </summary>
    public void AddAmmo(int amount)
    {
        if (HasInfiniteAmmo) return;

        // Не превышаем общий лимит
        int maxReserve = MaxAmmoCapacity - MagazineSize;
        ReserveAmmo = Mathf.Min(ReserveAmmo + amount, maxReserve);

        OnAmmoChanged?.Invoke();
    }

    #endregion
}