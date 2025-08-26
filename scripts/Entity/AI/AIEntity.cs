using Game.Entity.AI.Behaviors;
using Godot;

namespace Game.Entity.AI;

/// <summary>
/// Базовый класс для всех сущностей, управляемых ИИ.
/// Инкапсулирует логику навигации и машину состояний.
/// </summary>
public abstract partial class AIEntity : MoveableEntity
{
    [ExportGroup("AI Patrol Parameters")]
    [Export(PropertyHint.Range, "1, 20, 0.5")] public float BodyRotationSpeed { get; private set; } = 10f;
    [Export(PropertyHint.Range, "1, 20, 0.5")] public float HeadRotationSpeed { get; private set; } = 15f;
    [Export] public bool UseRandomPatrolRadius { get; private set; } = true;
    [Export] public float MinPatrolRadius { get; private set; } = 5f;
    [Export] public float MaxPatrolRadius { get; private set; } = 20f;

    [Export] public bool UseRandomWaitTime { get; private set; } = true;
    [Export(PropertyHint.Range, "0, 10, 0.5")] public float MinPatrolWaitTime { get; private set; } = 1.0f;
    [Export(PropertyHint.Range, "0, 10, 0.5")] public float MaxPatrolWaitTime { get; private set; } = 3.0f;

    [ExportGroup("Dependencies")]
    [Export] private NavigationAgent3D _navigationAgent;
    [Export] private Area3D _targetDetectionArea;
    [Export] private Node _combatBehaviorNode;
    [Export] private Node3D _headPivot;

    public NavigationAgent3D NavigationAgent => _navigationAgent;
    public LivingEntity CurrentTarget { get; private set; }
    public ICombatBehavior CombatBehavior { get; private set; }
    public Vector3 SpawnPosition { get; private set; }

    private State _currentState;
    private bool _isAiActive = false;

    protected AIEntity(IDs id) : base(id) { }

    public AIEntity() { } // Для Godot-компилятора

    public override async void _Ready()
    {
        base._Ready();
        SpawnPosition = GlobalPosition;

#if DEBUG
        // Валидация зависимостей
        if (!ValidateDependencies())
        {
            SetPhysicsProcess(false);
            return;
        }
#endif

        if (_targetDetectionArea != null)
        {
            _targetDetectionArea.BodyEntered += OnTargetDetected;
            _targetDetectionArea.BodyExited += OnTargetLost;
        }

        // Проверяем, готова ли навигация сразу. Если да - запускаем ИИ.
        if (GameManager.Instance.IsNavigationReady)
        {
            InitializeAI();
        }
        else
        {
            // Если нет - подписываемся на сигнал и ждем.
            // Используем 'await' чтобы код был более линейным и читаемым.
            GD.Print($"{Name} waiting for navigation map...");
            await ToSignal(GameManager.Instance, GameManager.SignalName.NavigationReady);
            InitializeAI();
        }
    }

    /// <summary>
    /// Централизованный метод для запуска машины состояний ИИ.
    /// Вызывается только после того, как навигация будет готова.
    /// </summary>
    private void InitializeAI()
    {
        if (_isAiActive) return;

        // --- КЛЮЧЕВОЙ ЗАЩИТНЫЙ МЕХАНИЗМ ---
        // Перед запуском ИИ, принудительно "примагничиваем" его к ближайшей
        // точке на NavMesh. Это решает проблему, если ИИ заспавнился
        // в миллиметре от сетки.
        // GlobalPosition = NavigationServer3D.MapGetClosestPoint(GetWorld3D().NavigationMap, GlobalPosition);

        GD.Print($"{Name} initializing AI, navigation map is ready.");
        _isAiActive = true;
        ChangeState(new PatrolState(this));
    }

    private bool ValidateDependencies()
    {
        if (_navigationAgent == null)
        {
            GD.PushError($"NavigationAgent3D dont be assigned to {Name}!");
            return false;
        }

        if (_headPivot == null)
        {
            // Это не критическая ошибка, а предупреждение, т.к. не у всех ИИ может быть голова
            GD.PushWarning($"HeadPivot is not assigned to {Name}. Head rotation methods will not work.");
        }

        if (_combatBehaviorNode is ICombatBehavior behavior)
        {
            CombatBehavior = behavior;
        }
        else
        {
            GD.PushError($"Node, assigned as CombatBehavior for {Name}, does not implement ICombatBehavior interface or is not assigned!");
            return false;
        }
        return true;
    }

#if DEBUG
    private void OnPathChanged()
    {
        GD.Print($"[{Name}] New path calculated! Points: {_navigationAgent.GetCurrentNavigationPath().Length}");
    }
#endif

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        _currentState?.Update((float)delta);

        Vector3 targetVelocity = Vector3.Zero;

        if (_isAiActive && !_navigationAgent.IsNavigationFinished())
        {
            var nextPoint = _navigationAgent.GetNextPathPosition();
            var direction = GlobalPosition.DirectionTo(nextPoint);
            targetVelocity = direction * Speed;
        }

        // Velocity рассчитывается с учетом гравитации из base._PhysicsProcess
        Velocity = Velocity.Lerp(targetVelocity, Acceleration * (float)delta);

        // Вращение тела в сторону движения
        var horizontalVelocity = Velocity with { Y = 0 };
        if (horizontalVelocity.LengthSquared() > 0.1f) // Вращаем, только если есть горизонтальное движение
        {
            // Плавно интерполируем вращение тела в сторону движения
            var targetRotation = Basis.LookingAt(horizontalVelocity.Normalized());
            Basis = Basis.Slerp(targetRotation, BodyRotationSpeed * (float)delta);
        }

        MoveAndSlide();
    }

    public void ChangeState(State newState)
    {
        _currentState?.Exit();
        _currentState = newState;
        _currentState.Enter();
    }

    public void SetAttackTarget(LivingEntity target)
    {
        if (target == this || target == null) return;

        GD.Print($"{Name} recvieve {target.Name}");
        CurrentTarget = target;

        if (_currentState is not AttackState)
        {
            ChangeState(new AttackState(this));
        }
    }

    public void ClearTarget()
    {
        CurrentTarget = null;
    }

    #region Movement API for States

    /// <summary>
    /// Устанавливает финальную цель для навигационного агента.
    /// Само движение теперь обрабатывается в _PhysicsProcess.
    /// </summary>
    public void MoveTo(Vector3 targetPosition)
    {
        if (_navigationAgent.TargetPosition == targetPosition) return;
        _navigationAgent.TargetPosition = targetPosition;
        GD.Print($"{Name} MoveTo: {targetPosition}");
    }

    /// <summary>
    /// Останавливает движение, устанавливая цель в текущую позицию.
    /// </summary>
    public void StopMovement()
    {
        // Самый простой способ остановить агента - сказать ему идти туда, где он уже стоит.
        _navigationAgent.TargetPosition = GlobalPosition;
        GD.Print("StopMovement");
    }

    #endregion

    #region Rotation API

    /// <summary>
    /// Плавно поворачивает тело ИИ в сторону указанной цели.
    /// Используется для ситуаций, когда ИИ стоит на месте, но должен смотреть на что-то.
    /// </summary>
    public void RotateBodyTowards(Vector3 targetPoint, float delta)
    {
        var direction = GlobalPosition.DirectionTo(targetPoint) with { Y = 0 };
        if (direction.IsZeroApprox())
        {
            var targetRotation = Basis.LookingAt(direction.Normalized());
            Basis = Basis.Slerp(targetRotation, BodyRotationSpeed * delta);
        }
    }

    /// <summary>
    /// Плавно поворачивает "голову" ИИ (дочерний узел) в сторону указанной цели.
    /// Тело при этом может двигаться в другую сторону.
    /// </summary>
    public void RotateHeadTowards(Vector3 targetPoint, float delta)
    {
        if (_headPivot == null) return;

        // Вращаем голову в ее локальном пространстве, чтобы она поворачивалась относительно тела
        var localTarget = _headPivot.ToLocal(targetPoint).Normalized();
        var targetRotation = Basis.LookingAt(localTarget);
        _headPivot.Basis = _headPivot.Basis.Slerp(targetRotation, HeadRotationSpeed * delta); // Голова обычно вращается быстрее тела
    }

    #endregion

    #region Signal Handlers

    private void OnTargetDetected(Node3D body)
    {
        if (CurrentTarget == null && body is LivingEntity entity && entity != this)
        {
            // TODO: Заменить на систему фракций
            if (entity.ID != IDs.Kaiju)
            {
                GD.Print($"{Name} find target: {entity.Name}");
                CurrentTarget = entity;
            }
        }
    }

    private void OnTargetLost(Node3D body)
    {
        if (body == CurrentTarget)
        {
            GD.Print($"{Name} lost target: {CurrentTarget.Name}");
            ClearTarget();
        }
    }

    #endregion
}