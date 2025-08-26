using System;
using Game.Entity.AI;
using Godot;

namespace Game.Entity.AI;

/// <summary>
/// Базовый класс для всех сущностей, управляемых ИИ.
/// Инкапсулирует логику навигации и машину состояний.
/// </summary>
public abstract partial class AIEntity : MoveableEntity
{
    [ExportGroup("AI Parameters")]
    [Export] public float AttackRange { get; private set; } = 2.5f;

    [ExportGroup("Dependencies")]
    [Export] private NavigationAgent3D _navigationAgent;
    [Export] private Area3D _targetDetectionArea;

    public LivingEntity CurrentTarget { get; private set; }

    private State _currentState;

    protected AIEntity(IDs id) : base(id) { }

    public AIEntity() { } // Для Godot-компилятора

    public override void _Ready()
    {
        base._Ready();

        if (_navigationAgent == null)
        {
            GD.PushError("NavigationAgent3D is not assigned to AIEntity!");
            SetPhysicsProcess(false);
            return;
        }

        // Подписываемся на события зоны обнаружения
        if (_targetDetectionArea != null)
        {
            _targetDetectionArea.BodyEntered += OnTargetDetected;
            _targetDetectionArea.BodyExited += OnTargetLost;
        }

        // Запускаем ИИ в состоянии патрулирования
        ChangeState(new PatrolState(this));
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        // Обновляем состояние ИИ
        _currentState?.Update(delta);

        MoveAndSlide();
    }

    /// <summary>
    /// Плавно меняет состояние ИИ, вызывая методы Exit() и Enter().
    /// </summary>
    public void ChangeState(State newState)
    {
        _currentState?.Exit();
        _currentState = newState;
        _currentState.Enter();
    }

    /// <summary>
    /// Задает цель для атаки извне (например, по клику игрока или по триггеру).
    /// ИИ немедленно перейдет в состояние атаки.
    /// </summary>
    public void SetAttackTarget(LivingEntity target)
    {
        if (target == this || target == null) return;

        GD.Print($"{Name} received external command to attack {target.Name}");
        CurrentTarget = target;

        // Если мы не в состоянии атаки, переключаемся на него.
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
    /// Вычисляет желаемую скорость для движения к цели и плавно интерполирует
    /// текущую скорость к целевой.
    /// </summary>
    public void MoveTowards(Vector3 targetPosition, float delta)
    {
        _navigationAgent.TargetPosition = targetPosition;
        Vector3 nextPathPosition = _navigationAgent.GetNextPathPosition();

        Vector3 direction = GlobalPosition.DirectionTo(nextPathPosition);
        direction.Y = 0; // ИИ контролирует только горизонтальное движение

        // Точно как в классе Player, создаем целевую горизонтальную скорость
        Vector3 targetVelocity = direction.Normalized() * Speed;

        // Плавно интерполируем текущую горизонтальную скорость к целевой
        Velocity = Velocity with
        {
            X = Mathf.Lerp(Velocity.X, targetVelocity.X, Acceleration * delta),
            Z = Mathf.Lerp(Velocity.Z, targetVelocity.Z, Acceleration * delta),
        };
    }

    /// <summary>
    /// Плавно останавливает горизонтальное движение с помощью интерполяции.
    /// </summary>
    public void StopMovement(float delta)
    {
        Velocity = Velocity with
        {
            X = Mathf.Lerp(Velocity.X, 0, Deceleration * delta),
            Z = Mathf.Lerp(Velocity.Z, 0, Deceleration * delta),
        };
    }

    #endregion

    #region Signal Handlers

    private void OnTargetDetected(Node3D body)
    {
        // Атакуем только другие живые сущности, но не себя. И только если у нас еще нет цели.
        if (CurrentTarget == null && body is LivingEntity entity && entity != this)
        {
            // Простая проверка - атакуем первого встречного, кто не Кайдзю.
            // Можно добавить логику фракций, приоритетов и т.д.
            if (entity.ID != IDs.Kaiju)
            {
                GD.Print($"{Name} detected a new target: {entity.Name}");
                CurrentTarget = entity;
            }
        }
    }

    private void OnTargetLost(Node3D body)
    {
        // Если именно наша цель покинула зону видимости, мы ее "забываем".
        if (body == CurrentTarget)
        {
            GD.Print($"{Name} lost sight of target: {CurrentTarget.Name}");
            ClearTarget();
        }
    }

    #endregion
}