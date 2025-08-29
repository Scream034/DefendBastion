// Game/Entity/AI/Behaviors/StationaryCombatBehavior.cs
using Game.Entity.AI.States;
using Game.Interfaces;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
        [Export(PropertyHint.Range, "3, 20, 1")] private float _repositionSearchRadius = 10f;
        [Export] private RepositionStrategy _repositionStrategy = RepositionStrategy.HybridAnalysis;

        [ExportGroup("Repositioning Maneuvers")]
        [Export] private bool _allowReposition = true;
        [Export] private bool _allowLiveSearch = true;
        [Export] private bool _liveSearchCanStrafe = true;
        [Export] private bool _liveSearchCanMoveForwardBack = true;
        [Export] private bool _liveSearchCanRotate = false;

        // Настройки Live Search
        [ExportGroup("Live Search Settings")]
        [Export(PropertyHint.Range, "0.5, 5.0, 0.1")] private float _liveSearchSegmentDuration = 1.5f; // Сколько AI движется в одном направлении
        [Export(PropertyHint.Range, "1.0, 10.0, 0.5")] private float _liveSearchTotalDuration = 5.0f; // Общее время на LiveSearch
        [Export(PropertyHint.Range, "0.1, 1.0, 0.1")] private float _liveSearchMovementMultiplier = 0.7f; // Насколько замедлен AI при LiveSearch

        [ExportGroup("Dependencies")]
        [Export] private Node _attackActionNode;

        private IAttackAction _attackAction;
        private double _timeSinceLastAttack = 0;
        private RepositioningSubState _currentRepositionSubState = RepositioningSubState.None;
        private Vector3 _repositionTargetPosition;
        private double _liveSearchSegmentTimer = 0;
        private double _liveSearchTotalTimer = 0;
        private Vector3 _liveSearchDirection = Vector3.Zero;
        private readonly List<RepositioningSubState> _availableLiveSearchActions = new();

        public override void _Ready()
        {
            if (_attackActionNode is IAttackAction action)
            {
                _attackAction = action;
            }
            else
            {
                GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!");
                SetPhysicsProcess(false);
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

            // Если ИИ активно меняет позицию, делегируем управление соответствующему методу
            if (_currentRepositionSubState != RepositioningSubState.None)
            {
                HandleRepositioning(context, target, delta);
                return;
            }

            // Если цель слишком далеко, сближаемся
            if (distanceToTarget > AttackRange)
            {
                context.MoveTo(target.GlobalPosition);
                return;
            }

            // Проверяем, есть ли чистый прострел от дула оружия
            bool hasClearShot = !_requireMuzzleLoS || _attackAction?.MuzzlePoint == null 
                || context.HasClearPath(
                    _attackAction.MuzzlePoint.GlobalPosition, 
                    target.GlobalPosition, 
                    [context.GetRid(), target.GetRid()]
                );

            if (hasClearShot)
            {
                // Если прострел есть, стоим на месте, целимся и стреляем
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

                bool foundNewPosition = false;

                // 1. Пытаемся рассчитать новую позицию, если разрешено
                if (_allowReposition && _attackAction?.MuzzlePoint != null)
                {
                    var weaponLocalOffset = _attackAction.MuzzlePoint.Position;
                    Vector3? newPos = _repositionStrategy switch
                    {
                        RepositionStrategy.HybridAnalysis => context.FindOptimalFiringPosition_Hybrid(target, weaponLocalOffset, _repositionSearchRadius),
                        _ => context.FindOptimalFiringPosition_Probing(target, weaponLocalOffset, _repositionSearchRadius),
                    };

                    if (newPos.HasValue)
                    {
                        _repositionTargetPosition = newPos.Value;
                        _currentRepositionSubState = RepositioningSubState.CalculatedMove;
                        context.MoveTo(_repositionTargetPosition);
                        foundNewPosition = true;
                    }
                }

                // 2. Если рассчитать не удалось (или запрещено), и разрешен "живой поиск", начинаем его
                if (!foundNewPosition && _allowLiveSearch)
                {
                    StartLiveSearch(context, target);
                }
                // 3. Если все маневры запрещены, просто стоим и целимся, ожидая возможности
                else if (!foundNewPosition)
                {
                    GD.Print($"{context.Name} cannot reposition, holding position and aiming.");
                    context.RotateBodyTowards(target.GlobalPosition, (float)delta);
                    context.RotateHeadTowards(target.GlobalPosition, (float)delta);
                }
            }
        }

        private void HandleRepositioning(AIEntity context, PhysicsBody3D target, double delta)
        {
            // Постоянно проверяем, не появился ли прострел во время движения
            if (_requireMuzzleLoS && _attackAction?.MuzzlePoint != null)
            {
                if (context.HasClearPath(_attackAction.MuzzlePoint.GlobalPosition, target.GlobalPosition, [context.GetRid(), target.GetRid()]))
                {
                    GD.Print($"{context.Name} reacquired clear shot during repositioning!");
                    ResetRepositioningState(context);
                    return;
                }
            }
            
            // Во время любого маневра всегда смотрим на цель
            context.RotateBodyTowards(target.GlobalPosition, (float)delta);
            context.RotateHeadTowards(target.GlobalPosition, (float)delta);

            switch (_currentRepositionSubState)
            {
                case RepositioningSubState.CalculatedMove:
                    if (context.NavigationAgent.IsNavigationFinished())
                    {
                        GD.Print($"{context.Name} reached calculated position. Still no clear shot.");
                        // Если дошли, но выстрела нет, и разрешен живой поиск, начинаем его
                        if (_allowLiveSearch)
                        {
                            StartLiveSearch(context, target);
                        }
                        else // Иначе сдаемся и сбрасываем состояние
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
                    }

                    PerformLiveSearchMovement(context);
                    break;
            }
        }

        private void StartLiveSearch(AIEntity context, PhysicsBody3D target)
        {
            // Собираем список доступных действий на основе настроек
            _availableLiveSearchActions.Clear();
            if (_liveSearchCanStrafe) _availableLiveSearchActions.Add(RepositioningSubState.LiveSearchStrafe);
            if (_liveSearchCanMoveForwardBack) _availableLiveSearchActions.Add(RepositioningSubState.LiveSearchForward);
            if (_liveSearchCanRotate) _availableLiveSearchActions.Add(RepositioningSubState.LiveSearchRotate);

            // Если ни одного действия не разрешено, выходим
            if (!_availableLiveSearchActions.Any())
            {
                GD.Print($"{context.Name} has no available LiveSearch actions.");
                ResetRepositioningState(context);
                return;
            }

            _liveSearchTotalTimer = _liveSearchTotalDuration;
            context.SetMovementSpeed(context.NormalSpeed * _liveSearchMovementMultiplier);
            ChooseNextLiveSearchAction(context, target); // Выбираем первое действие
        }

        private void ChooseNextLiveSearchAction(AIEntity context, PhysicsBody3D target)
        {
            _liveSearchSegmentTimer = _liveSearchSegmentDuration;
            
            if (!_availableLiveSearchActions.Any())
            {
                ResetRepositioningState(context);
                return;
            }

            // Выбираем случайное действие из списка разрешенных
            _currentRepositionSubState = _availableLiveSearchActions[Random.Shared.Next(0, _availableLiveSearchActions.Count)];

            var directionToTarget = context.GlobalPosition.DirectionTo(target.GlobalPosition).Normalized();

            switch (_currentRepositionSubState)
            {
                case RepositioningSubState.LiveSearchStrafe:
                    _liveSearchDirection = directionToTarget.Cross(Vector3.Up).Normalized() * (Random.Shared.Next(0, 2) * 2 - 1);
                    GD.Print($"{context.Name} LiveSearch: Strafe {_liveSearchDirection}");
                    break;
                case RepositioningSubState.LiveSearchForward:
                    _liveSearchDirection = directionToTarget * (Random.Shared.Next(0, 2) * 2 - 1);
                    GD.Print($"{context.Name} LiveSearch: Move {_liveSearchDirection}");
                    break;
                case RepositioningSubState.LiveSearchRotate:
                    _liveSearchDirection = Vector3.Zero; // Для поворота движение не нужно
                    GD.Print($"{context.Name} LiveSearch: Rotate");
                    break;
            }
            context.StopMovement();
        }

        private void PerformLiveSearchMovement(AIEntity context)
        {
            if (_currentRepositionSubState == RepositioningSubState.LiveSearchRotate)
            {
                // Поворот тела уже обрабатывается общим кодом в HandleRepositioning,
                // поэтому здесь ничего дополнительного не требуется.
                return;
            }
            
            // Для стрейфа и движения вперед/назад задаем цель для навигации
            if (!_liveSearchDirection.IsZeroApprox())
            {
                var movementTarget = context.GlobalPosition + _liveSearchDirection * context.Speed * (float)_liveSearchSegmentDuration;
                context.MoveTo(NavigationServer3D.MapGetClosestPoint(context.GetWorld3D().NavigationMap, movementTarget));
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