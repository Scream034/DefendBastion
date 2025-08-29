// Game/Entity/AI/Behaviors/StationaryCombatBehavior.cs
using Game.Entity.AI.States;
using Game.Interfaces;
using Godot;
using System;

namespace Game.Entity.AI.Behaviors
{
    // Как ИИ ищет новую позицию для стрельбы, если текущая заблокирована.
    internal enum FiringPositionSearchMode
    {
        // Мгновенно вычисляет наилучшую точку в радиусе. Эффективно.
        Calculate,
        // ИИ активно двигается (например, стрейфит), чтобы найти прострел.
        LiveSearch
    }

    // Внутренние подсостояния, когда AI ищет позицию
    internal enum RepositioningSubState
    {
        None,               // Не ищем
        CalculatedMove,     // Движемся к расчетной точке
        LiveSearchStrafe,   // Активно стрейфим
        LiveSearchForward,  // Активно движемся вперед/назад
        LiveSearchRotate    // Поворачиваемся, чтобы осмотреться
    }

    /// <summary>
    /// Стратегия, используемая для поиска новой огневой позиции.
    /// </summary>
    internal enum RepositionStrategy
    {
        /// <summary>Простое зондирование по флангам.</summary>
        StandardProbing,
        /// <summary>Анализ препятствия для более умного поиска.</summary>
        HybridAnalysis
    }

    public partial class StationaryCombatBehavior : Node, ICombatBehavior
    {
        [ExportGroup("Behavior Settings")]
        [Export] public float AttackRange { get; private set; } = 15f;
        [Export] public float AttackCooldown { get; private set; } = 2.0f;

        [ExportGroup("Line of Fire Settings")]
        [Export] private bool _requireMuzzleLoS = true;
        [Export] private FiringPositionSearchMode _searchMode = FiringPositionSearchMode.Calculate;
        [Export(PropertyHint.Range, "3, 20, 1")] private float _repositionSearchRadius = 10f;
        [Export] private RepositionStrategy _repositionStrategy = RepositionStrategy.HybridAnalysis;

        // Настройки Live Search
        [ExportGroup("Live Search Settings")]
        [Export(PropertyHint.Range, "0.5, 5.0, 0.1")] private float _liveSearchSegmentDuration = 1.5f; // Сколько AI движется в одном направлении
        [Export(PropertyHint.Range, "1.0, 10.0, 0.5")] private float _liveSearchTotalDuration = 5.0f; // Общее время на LiveSearch
        [Export] private float _liveSearchMovementMultiplier = 0.7f; // Насколько замедлен AI при LiveSearch (относительно NormalSpeed)

        [ExportGroup("Dependencies")]
        [Export] private Node _attackActionNode;

        private IAttackAction _attackAction;
        private double _timeSinceLastAttack = 0;
        private RepositioningSubState _currentRepositionSubState = RepositioningSubState.None;
        private Vector3 _repositionTargetPosition; // Цель для CalculatedMove или LiveSearch
        private double _liveSearchSegmentTimer = 0;
        private double _liveSearchTotalTimer = 0;
        private Vector3 _liveSearchDirection = Vector3.Zero; // Направление для стрейфа/движения

        public override void _Ready()
        {
            if (_attackActionNode is IAttackAction action)
            {
                _attackAction = action;
            }
            else
            {
                GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!");
                SetPhysicsProcess(false); // Отключаем, если нет поведения атаки
                return;
            }
            _timeSinceLastAttack = AttackCooldown;
        }

        public void Process(AIEntity context, double delta)
        {
            if (context.CurrentTarget == null || !GodotObject.IsInstanceValid(context.CurrentTarget))
            {
                ResetRepositioningState(context);
                context.ReturnToDefaultState();
                return;
            }

            _timeSinceLastAttack += delta;
            var target = context.CurrentTarget;
            float distanceToTarget = context.GlobalPosition.DistanceTo(target.GlobalPosition);

            if (_currentRepositionSubState != RepositioningSubState.None)
            {
                HandleRepositioning(context, target, delta);
                return;
            }

            if (distanceToTarget > AttackRange)
            {
                context.MoveTo(target.GlobalPosition);
                return;
            }

            bool hasClearShot = true;
            if (_requireMuzzleLoS && _attackAction?.MuzzlePoint != null)
            {
                var exclusionList = new Godot.Collections.Array<Rid> { context.GetRid(), target.GetRid() };
                hasClearShot = context.HasClearPath(_attackAction.MuzzlePoint.GlobalPosition, target.GlobalPosition, exclusionList);
            }

            if (hasClearShot)
            {
                context.StopMovement();
                context.RotateBodyTowards(target.GlobalPosition, (float)delta);
                context.RotateHeadTowards(target.GlobalPosition, (float)delta);

                if (_timeSinceLastAttack >= AttackCooldown)
                {
                    _attackAction?.Execute(context, context.CurrentTarget);
                    _timeSinceLastAttack = 0;
                }
            }
            else // ЛИНИЯ ВЫСТРЕЛА ЗАБЛОКИРОВАНА
            {
                GD.Print($"{context.Name}'s muzzle is blocked. Attempting to find a new firing position.");
                context.StopMovement();

                if (_attackAction?.MuzzlePoint != null)
                {
                    var weaponLocalOffset = _attackAction.MuzzlePoint.Position;
                    Vector3? newPos;

                    // ВЫБОР СТРАТЕГИИ ПОИСКА
                    switch (_repositionStrategy)
                    {
                        case RepositionStrategy.HybridAnalysis:
                            newPos = context.FindOptimalFiringPosition_Hybrid(target, weaponLocalOffset, _repositionSearchRadius);
                            break;
                        case RepositionStrategy.StandardProbing:
                        default:
                            newPos = context.FindOptimalFiringPosition_Probing(target, weaponLocalOffset, _repositionSearchRadius);
                            break;
                    }

                    if (newPos.HasValue)
                    {
                        _repositionTargetPosition = newPos.Value;
                        _currentRepositionSubState = RepositioningSubState.CalculatedMove;
                        context.MoveTo(_repositionTargetPosition);
                        return;
                    }
                    else
                    {
                        GD.Print($"{context.Name} could not find a clear firing position via calculation.");
                    }
                }

                if (_searchMode == FiringPositionSearchMode.LiveSearch || _currentRepositionSubState == RepositioningSubState.None)
                {
                    StartLiveSearch(context, target);
                }
                else
                {
                    context.RotateBodyTowards(target.GlobalPosition, (float)delta);
                    context.RotateHeadTowards(target.GlobalPosition, (float)delta);
                }
            }
        }

        private void HandleRepositioning(AIEntity context, PhysicsBody3D target, double delta)
        {
            if (_requireMuzzleLoS && _attackAction?.MuzzlePoint != null)
            {
                var exclusionList = new Godot.Collections.Array<Rid> { context.GetRid(), target.GetRid() };
                if (context.HasClearPath(_attackAction.MuzzlePoint.GlobalPosition, target.GlobalPosition, exclusionList))
                {
                    GD.Print($"{context.Name} reacquired clear shot during repositioning!");
                    ResetRepositioningState(context);
                    return;
                }
            }

            switch (_currentRepositionSubState)
            {
                case RepositioningSubState.CalculatedMove:
                    if (context.NavigationAgent.IsNavigationFinished())
                    {
                        GD.Print($"{context.Name} reached calculated position. Still no clear shot.");
                        if (_searchMode == FiringPositionSearchMode.LiveSearch)
                        {
                            StartLiveSearch(context, target);
                        }
                        else
                        {
                            ResetRepositioningState(context);
                        }
                    }
                    break;

                case RepositioningSubState.LiveSearchStrafe:
                case RepositioningSubState.LiveSearchForward:
                case RepositioningSubState.LiveSearchRotate:
                    _liveSearchTotalTimer -= delta;
                    _liveSearchSegmentTimer -= delta;

                    if (_liveSearchTotalTimer <= 0)
                    {
                        GD.Print($"{context.Name} LiveSearch timed out. Giving up.");
                        ResetRepositioningState(context);
                        return;
                    }

                    if (_liveSearchSegmentTimer <= 0)
                    {
                        ChooseNextLiveSearchAction(context, target);
                        _liveSearchSegmentTimer = _liveSearchSegmentDuration;
                    }

                    PerformLiveSearchMovement(context, target, (float)delta);
                    break;
            }
        }

        private void StartLiveSearch(AIEntity context, PhysicsBody3D target)
        {
            _liveSearchTotalTimer = _liveSearchTotalDuration;
            _liveSearchSegmentTimer = 0;
            ChooseNextLiveSearchAction(context, target);
            context.SetMovementSpeed(context.NormalSpeed * _liveSearchMovementMultiplier);
        }

        private void ChooseNextLiveSearchAction(AIEntity context, PhysicsBody3D target)
        {
            var r = Random.Shared.Next(0, 100);
            if (r < 40) // 40% шанс на стрейф
            {
                _currentRepositionSubState = RepositioningSubState.LiveSearchStrafe;
                var directionToTarget = context.GlobalPosition.DirectionTo(target.GlobalPosition);
                _liveSearchDirection = directionToTarget.Cross(Vector3.Up).Normalized() * (Random.Shared.NextDouble() > 0.5 ? 1 : -1);
                GD.Print($"{context.Name} LiveSearch: Strafe {_liveSearchDirection}");
            }
            else if (r < 80) // 40% шанс на движение вперед/назад
            {
                _currentRepositionSubState = RepositioningSubState.LiveSearchForward;
                _liveSearchDirection = context.GlobalPosition.DirectionTo(target.GlobalPosition).Normalized() * (Random.Shared.NextDouble() > 0.5 ? 1 : -1);
                GD.Print($"{context.Name} LiveSearch: Forward {_liveSearchDirection}");
            }
            else // 20% шанс на поворот на месте
            {
                _currentRepositionSubState = RepositioningSubState.LiveSearchRotate;
                _liveSearchDirection = Vector3.Up * (Random.Shared.NextDouble() > 0.5 ? 1 : -1); // Направление поворота
                GD.Print($"{context.Name} LiveSearch: Rotate {_liveSearchDirection}");
            }
            context.StopMovement();
        }

        private void PerformLiveSearchMovement(AIEntity context, PhysicsBody3D target, float delta)
        {
            context.RotateBodyTowards(target.GlobalPosition, delta);
            context.RotateHeadTowards(target.GlobalPosition, delta);

            switch (_currentRepositionSubState)
            {
                case RepositioningSubState.LiveSearchStrafe:
                case RepositioningSubState.LiveSearchForward:
                    var movementTarget = context.GlobalPosition + _liveSearchDirection * context.Speed * (float)_liveSearchSegmentDuration;
                    context.MoveTo(NavigationServer3D.MapGetClosestPoint(context.GetWorld3D().NavigationMap, movementTarget));
                    break;
                case RepositioningSubState.LiveSearchRotate:
                    context.Basis = context.Basis.Rotated(Vector3.Up, (float)_liveSearchDirection.Y * context.BodyRotationSpeed * delta);
                    break;
            }
        }

        private void ResetRepositioningState(AIEntity context)
        {
            _currentRepositionSubState = RepositioningSubState.None;
            _repositionTargetPosition = Vector3.Zero;
            _liveSearchSegmentTimer = 0;
            _liveSearchTotalTimer = 0;
            _liveSearchDirection = Vector3.Zero;
            context.StopMovement();
            context.SetMovementSpeed(context.NormalSpeed);
            GD.Print($"{context.Name} Repositioning state reset.");
        }
    }
}