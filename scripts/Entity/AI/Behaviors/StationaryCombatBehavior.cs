using Godot;
using System.Collections.Generic;
using System.Linq;
using System;
using Game.Entity.AI.Components;

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
        private double _allyRepositionCooldown = 0;
        private const double ALLY_REPOSITION_COOLDOWN_TIME = 1.5;

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

        public bool Process(AIEntity context, double delta)
        {
            if (context.TargetingSystem.CurrentTarget is not LivingEntity target || !IsInstanceValid(target))
            {
                // Если цель стала невалидной, сообщаем об этом, но это не тактическая неудача
                context.ReturnToDefaultState();
                return true;
            }

            _timeSinceLastAttack += delta;
            _allyRepositionCooldown -= delta;

            if (_currentRepositionSubState != RepositioningSubState.None)
            {
                return HandleRepositioning(context, target, delta);
            }

            var fromPosition = _attackAction?.MuzzlePoint?.GlobalPosition ?? context.GlobalPosition;
            uint losMask = context.Profile?.CombatProfile?.LineOfSightMask ?? 1;

            var losResult = AITacticalAnalysis.AnalyzeLineOfSight(context, fromPosition, target, losMask, out _);

            switch (losResult)
            {
                case LoSAnalysisResult.Clear:
                    context.MovementController.StopMovement();
                    if (_timeSinceLastAttack >= AttackCooldown)
                    {
                        _attackAction?.Execute(context, target, target.GlobalPosition);
                        _timeSinceLastAttack = 0;
                    }
                    break;

                case LoSAnalysisResult.BlockedByAlly:
                    if (_allyRepositionCooldown <= 0)
                    {
                        var sidestepPos = AITacticalAnalysis.FindSidestepPosition(context, target);
                        if (sidestepPos.HasValue)
                        {
                            context.MovementController.MoveTo(sidestepPos.Value);
                            _allyRepositionCooldown = ALLY_REPOSITION_COOLDOWN_TIME;
                        }
                    }
                    break;

                case LoSAnalysisResult.BlockedByObstacle:
                    if (!AttemptReposition(context, target))
                    {
                        GD.Print($"{context.Name} failed to find a repositioning point and cannot engage.");
                        return false; // Сигнализируем о тактической неудаче
                    }
                    break;
            }
            return true; // Бой продолжается
        }

        private bool AttemptReposition(AIEntity context, PhysicsBody3D target)
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

            if (!foundNewPosition && _allowLiveSearch)
            {
                StartLiveSearch(context, target);
                return true; // Мы начали поиск, так что это еще не провал
            }

            return foundNewPosition; // Возвращаем, удалось ли нам хоть что-то предпринять
        }

        private bool HandleRepositioning(AIEntity context, PhysicsBody3D target, double delta)
        {
            if (!IsInstanceValid(target) || target is not LivingEntity livingTarget)
            {
                ResetRepositioningState(context);
                return true;
            }

            var muzzlePoint = _attackAction?.MuzzlePoint;
            if (_requireMuzzleLoS && muzzlePoint != null)
            {
                uint losMask = context.Profile?.CombatProfile?.LineOfSightMask ?? 1;
                if (AITacticalAnalysis.AnalyzeLineOfSight(context, muzzlePoint.GlobalPosition, livingTarget, losMask, out _) == LoSAnalysisResult.Clear)
                {
                    ResetRepositioningState(context);
                    return true; // Нашли цель, возвращаемся к обычной атаке
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
                    if (_liveSearchTotalTimer <= 0)
                    {
                        GD.Print($"{context.Name} live search timed out. Target not found.");
                        ResetRepositioningState(context);
                        return false; // Поиск провалился, сигнализируем о неудаче
                    }
                    if (_liveSearchSegmentTimer <= 0) ChooseNextLiveSearchAction(context, target);
                    PerformLiveSearchMovement(context);
                    break;
            }
            return true; // Процесс репозиционирования продолжается
        }

        // ... (остальные методы без изменений: StartLiveSearch, ChooseNextLiveSearchAction, PerformLiveSearchMovement, ResetRepositioningState) ...
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