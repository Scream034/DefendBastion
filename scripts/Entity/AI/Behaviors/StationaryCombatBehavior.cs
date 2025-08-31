using Godot;
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
        [Export] private Node _attackActionNode;

        public IAttackAction Action { get; private set; }

        private double _timeSinceLastAttack;
        private double _effectiveAttackCooldown;
        private bool _isRepositioning = false;

        public override void _Ready()
        {
            if (_attackActionNode is IAttackAction action) Action = action;
            else { GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!"); SetProcess(false); return; }

            var variance = (float)GD.RandRange(-0.1, 0.1);
            _effectiveAttackCooldown = AttackCooldown * (1.0f + variance);
            _timeSinceLastAttack = _effectiveAttackCooldown;
        }

        public void EnterCombat(AIEntity context)
        {
            _timeSinceLastAttack = _effectiveAttackCooldown;
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
                context.ReturnToDefaultState();
                return true;
            }

            _timeSinceLastAttack += delta;

            if (_isRepositioning)
            {
                if (context.MovementController.NavigationAgent.IsNavigationFinished())
                {
                    ResetRepositioningState(context);
                }
                else if (CanSeeTarget(context, target))
                {
                    ResetRepositioningState(context);
                }
                return true;
            }

            // Проверяем, не назначил ли нам уже координатор позицию.
            if (AISquadCoordinator.TryGetAssignedPosition(context, out var assignedPos))
            {
                StartReposition(context, assignedPos);
                return true;
            }

            var fromPosition = Action?.MuzzlePoint?.GlobalPosition ?? context.GlobalPosition;
            var losResult = AITacticalAnalysis.AnalyzeLineOfSight(context, fromPosition, target, context.Profile.CombatProfile.LineOfSightMask, out _);

            switch (losResult)
            {
                case LoSAnalysisResult.Clear:
                    context.MovementController.StopMovement();
                    if (_timeSinceLastAttack >= _effectiveAttackCooldown)
                    {
                        Action?.Execute(context, target, target.GlobalPosition);
                        _timeSinceLastAttack = 0;
                    }
                    break;

                case LoSAnalysisResult.BlockedByAlly:
                case LoSAnalysisResult.BlockedByObstacle:
                    if (!AttemptSquadReposition(context, target))
                    {
                        // Если даже скоординированный маневр невозможен, тактическая цель потеряна.
                        return false;
                    }
                    break;
            }
            return true;
        }

        private bool AttemptSquadReposition(AIEntity context, LivingEntity target)
        {
            // Этот AI становится инициатором перегруппировки.
            var nearbyAllies = context.GetNearbyAllies(target, AttackRange * 1.5f);

            // Включаем себя в список, если нас там еще нет.
            if (!nearbyAllies.Contains(context))
            {
                nearbyAllies.Add(context);
            }

            // Запрашиваем у координатора план для всей группы.
            return AISquadCoordinator.RequestPositionsForSquad(nearbyAllies, target);
        }

        private void StartReposition(AIEntity context, Vector3 targetPosition)
        {
            _isRepositioning = true;
            context.MovementController.MoveTo(targetPosition);
            GD.Print($"{context.Name} moving to assigned squad position {targetPosition}.");
        }

        private void ResetRepositioningState(AIEntity context)
        {
            if (_isRepositioning || AISquadCoordinator.TryGetAssignedPosition(context, out _))
            {
                AISquadCoordinator.ReleasePosition(context);
            }
            _isRepositioning = false;
            context.MovementController.StopMovement();
        }

        private bool CanSeeTarget(AIEntity context, LivingEntity target)
        {
            var fromPosition = Action?.MuzzlePoint?.GlobalPosition ?? context.GlobalPosition;
            return AITacticalAnalysis.AnalyzeLineOfSight(context, fromPosition, target, context.Profile.CombatProfile.LineOfSightMask, out _) == LoSAnalysisResult.Clear;
        }
    }
}