using Godot;
using System.Collections.Generic;
using System.Linq;
using Game.Entity.AI.Utils;
using System;

namespace Game.Entity.AI.Behaviors
{
    internal enum RepositionStrategy { StandardProbing, HybridAnalysis }
    internal enum RepositioningSubState { None, CalculatedMove, LiveSearchStrafe, LiveSearchForward, LiveSearchRotate }

    public partial class StationaryCombatBehavior : Node, ICombatBehavior
    {
        [Export] public float AttackRange { get; private set; } = 15f;
        [Export] public float AttackCooldown { get; private set; } = 2.0f;
        [Export] private bool _requireMuzzleLoS = true;
        [Export(PropertyHint.Range, "3, 20, 1")] private float _repositionSearchRadius = 10f;
        [Export] private RepositionStrategy _repositionStrategy = RepositionStrategy.HybridAnalysis;
        [Export] private bool _allowReposition = true;
        [Export] private bool _allowLiveSearch = true;
        [Export] private bool _liveSearchCanStrafe = true;
        [Export] private bool _liveSearchCanMoveForwardBack = true;
        [Export] private bool _liveSearchCanRotate = false;
        [Export(PropertyHint.Range, "0.5, 5.0, 0.1")] private float _liveSearchSegmentDuration = 1.5f;
        [Export(PropertyHint.Range, "1.0, 10.0, 0.5")] private float _liveSearchTotalDuration = 5.0f;
        [Export(PropertyHint.Range, "0.1, 1.0, 0.1")] private float _liveSearchMovementMultiplier = 0.7f;
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
            if (_attackActionNode is IAttackAction action) _attackAction = action;
            else { GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!"); SetProcess(false); }
            _timeSinceLastAttack = AttackCooldown;
        }

        public void EnterCombat(AIEntity context)
        {
            _timeSinceLastAttack = AttackCooldown;
            ResetRepositioningState(context);
        }

        public void ExitCombat(AIEntity context)
        {
            ResetRepositioningState(context);
        }

        public void Process(AIEntity context, double delta)
        {
            var target = context.TargetingSystem.CurrentTarget;
            if (!IsInstanceValid(target))
            {
                context.ReturnToDefaultState();
                return;
            }

            _timeSinceLastAttack += delta;

            // Если мы находимся в процессе смены позиции, делегируем управление соответствующему методу.
            if (_currentRepositionSubState != RepositioningSubState.None)
            {
                HandleRepositioning(context, target, delta);
                return;
            }

            // Логика движения к цели теперь находится в AttackState. Этот класс работает, когда цель уже в радиусе атаки.

            var muzzlePoint = _attackAction?.MuzzlePoint;
            Vector3? visiblePoint = null;

            // Используем кэшированный метод AIEntity для проверки видимости.
            // Приоритет - проверка от дула, если это требуется.
            if (_requireMuzzleLoS && muzzlePoint != null)
            {
                visiblePoint = context.GetVisibleTargetPointFrom(target, muzzlePoint.GlobalPosition);
            }
            else
            {
                visiblePoint = context.GetVisibleTargetPoint(target);
            }

            if (visiblePoint.HasValue) // Если цель видна (или ее часть)
            {
                context.MovementController.StopMovement();
                if (_timeSinceLastAttack >= AttackCooldown)
                {
                    _attackAction?.Execute(context, target, visiblePoint.Value);
                    _timeSinceLastAttack = 0;
                }
            }
            else // Если ничего не видно с текущей позиции
            {
                AttemptReposition(context, target);
            }
        }

        private void AttemptReposition(AIEntity context, PhysicsBody3D target)
        {
            context.MovementController.StopMovement();
            bool foundNewPosition = false;

            if (_allowReposition && _attackAction?.MuzzlePoint != null)
            {
                var weaponLocalOffset = _attackAction.MuzzlePoint.Position;
                Vector3? newPos = _repositionStrategy switch
                {
                    RepositionStrategy.HybridAnalysis => AITacticalAnalysis.FindOptimalFiringPosition_Hybrid(context, target, weaponLocalOffset, _repositionSearchRadius),
                    _ => AITacticalAnalysis.FindOptimalFiringPosition_Probing(context, target, weaponLocalOffset, _repositionSearchRadius),
                };

                if (newPos.HasValue)
                {
                    _repositionTargetPosition = newPos.Value;
                    _currentRepositionSubState = RepositioningSubState.CalculatedMove;
                    context.MovementController.MoveTo(_repositionTargetPosition);
                    foundNewPosition = true;
                }
            }

            if (!foundNewPosition && _allowLiveSearch) StartLiveSearch(context, target);
            else if (!foundNewPosition) GD.Print($"{context.Name} cannot reposition, holding position.");
        }

        private void HandleRepositioning(AIEntity context, PhysicsBody3D target, double delta)
        {
            if (!IsInstanceValid(target) || target is not LivingEntity livingTarget)
            {
                ResetRepositioningState(context);
                return;
            }

            var muzzlePoint = _attackAction?.MuzzlePoint;
            // Если во время движения к новой точке мы УЖЕ увидели цель, останавливаемся и начинаем атаковать.
            if (_requireMuzzleLoS && muzzlePoint != null)
            {
                // Используем централизованный метод для проверки
                if (context.GetVisibleTargetPointFrom(livingTarget, muzzlePoint.GlobalPosition).HasValue)
                {
                    ResetRepositioningState(context);
                    return;
                }
            }

            switch (_currentRepositionSubState)
            {
                case RepositioningSubState.CalculatedMove:
                    if (context.MovementController.NavigationAgent.IsNavigationFinished())
                    {
                        if (_allowLiveSearch) StartLiveSearch(context, target);
                        else ResetRepositioningState(context);
                    }
                    break;
                case RepositioningSubState.LiveSearchStrafe:
                case RepositioningSubState.LiveSearchForward:
                case RepositioningSubState.LiveSearchRotate:
                    _liveSearchTotalTimer -= delta;
                    _liveSearchSegmentTimer -= delta;
                    if (_liveSearchTotalTimer <= 0) { ResetRepositioningState(context); return; }
                    if (_liveSearchSegmentTimer <= 0) ChooseNextLiveSearchAction(context, target);
                    PerformLiveSearchMovement(context);
                    break;
            }
        }

        private void StartLiveSearch(AIEntity context, PhysicsBody3D target)
        {
            _availableLiveSearchActions.Clear();
            if (_liveSearchCanStrafe) _availableLiveSearchActions.Add(RepositioningSubState.LiveSearchStrafe);
            if (_liveSearchCanMoveForwardBack) _availableLiveSearchActions.Add(RepositioningSubState.LiveSearchForward);
            if (_liveSearchCanRotate) _availableLiveSearchActions.Add(RepositioningSubState.LiveSearchRotate);
            if (!_availableLiveSearchActions.Any()) { ResetRepositioningState(context); return; }

            _liveSearchTotalTimer = _liveSearchTotalDuration;
            context.SetMovementSpeed(context.Profile.MovementProfile.NormalSpeed * _liveSearchMovementMultiplier);
            ChooseNextLiveSearchAction(context, target);
        }

        private void ChooseNextLiveSearchAction(AIEntity context, PhysicsBody3D target)
        {
            _liveSearchSegmentTimer = _liveSearchSegmentDuration;
            if (!_availableLiveSearchActions.Any() || !IsInstanceValid(target)) { ResetRepositioningState(context); return; }

            _currentRepositionSubState = _availableLiveSearchActions[Random.Shared.Next(0, _availableLiveSearchActions.Count)];
            var directionToTarget = context.GlobalPosition.DirectionTo(target.GlobalPosition).Normalized();

            _liveSearchDirection = _currentRepositionSubState switch
            {
                RepositioningSubState.LiveSearchStrafe => directionToTarget.Cross(Vector3.Up).Normalized() * (Random.Shared.Next(0, 2) * 2 - 1),
                RepositioningSubState.LiveSearchForward => directionToTarget * (Random.Shared.Next(0, 2) * 2 - 1),
                _ => Vector3.Zero,
            };
            context.MovementController.StopMovement();
        }

        private void PerformLiveSearchMovement(AIEntity context)
        {
            if (_currentRepositionSubState != RepositioningSubState.LiveSearchRotate && !_liveSearchDirection.IsZeroApprox())
            {
                var movementTarget = context.GlobalPosition + _liveSearchDirection * context.Speed * (float)_liveSearchSegmentDuration;
                context.MovementController.MoveTo(NavigationServer3D.MapGetClosestPoint(context.GetWorld3D().NavigationMap, movementTarget));
            }
        }

        private void ResetRepositioningState(AIEntity context)
        {
            _currentRepositionSubState = RepositioningSubState.None;
            context.MovementController.StopMovement();
            context.SetMovementSpeed(context.Profile.MovementProfile.NormalSpeed);
        }
    }
}