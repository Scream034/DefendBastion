// --- ИЗМЕНЕНИЯ ---
// 1. Оптимизация `CalculateThreatScore`: вместо вызова `GetVisibleTargetPoint`, который может
//    делать несколько рейкастов по разным точкам, теперь используется один быстрый рейкаст
//    в центр цели с помощью AITacticalAnalysis.HasClearPath. Это ускоряет процесс
//    переоценки целей, особенно когда их много.
// -----------------

using System.Collections.Generic;
using Godot;
using Game.Interfaces;
using Game.Turrets;
using Game.Entity.AI.Components;

namespace Game.Entity.AI
{
    public static class AITargetEvaluator
    {
        private const float DistanceWeight = 2f;
        private const float LowHealthBonusMultiplier = 0.5f;
        private const float OccupiedTurretPriorityMultiplier = 3.0f;
        private const float TurretPriorityMultiplier = 1.5f;
        private const float EmptyTurretPriorityMultiplier = 0.5f;
        private const float DefaultPriorityMultiplier = 1.0f;

        public static LivingEntity GetBestTarget(AIEntity evaluator, IReadOnlyList<LivingEntity> potentialTargets)
        {
            LivingEntity bestTargetObject = null;
            float highestScore = -1f;

            foreach (var potentialTarget in potentialTargets)
            {
                if (!GodotObject.IsInstanceValid(potentialTarget)) continue;

                var effectiveTarget = GetEffectiveTarget(potentialTarget, evaluator);
                if (effectiveTarget == null || !GodotObject.IsInstanceValid(effectiveTarget)) continue;

                float currentScore = CalculateThreatScore(evaluator, effectiveTarget, World.DirectSpaceState);
                if (currentScore > highestScore)
                {
                    highestScore = currentScore;
                    bestTargetObject = effectiveTarget;
                }
            }
            return bestTargetObject;
        }

        private static LivingEntity GetEffectiveTarget(LivingEntity potentialTarget, AIEntity evaluator)
        {
            if (potentialTarget is Player.Player player && player.IsInTurret())
            {
                var turret = player.CurrentTurret;
                if (turret != null && turret.IsHostile(evaluator))
                {
                    return turret;
                }
                return null;
            }
            return potentialTarget;
        }

        private static float CalculateThreatScore(AIEntity evaluator, LivingEntity target, PhysicsDirectSpaceState3D spaceState)
        {
            var fromPos = evaluator.EyesPosition?.GlobalPosition ?? evaluator.GlobalPosition;
            var toPos = target.GlobalPosition; // Для оценки достаточно центра цели
            uint mask = evaluator.Profile?.CombatProfile?.LineOfSightMask ?? 1;
            var exclude = new Godot.Collections.Array<Rid> { evaluator.GetRid(), target.GetRid() };

            // Оптимизация: используем один быстрый рейкаст вместо GetFirstVisiblePoint
            if (!AITacticalAnalysis.HasClearPath(spaceState, fromPos, toPos, exclude, mask))
            {
                return -1f; // Цель не видна
            }

            float priorityMultiplier;

            if (target is ControllableTurret cTurret)
            {
                priorityMultiplier = cTurret.CurrentController != null ? OccupiedTurretPriorityMultiplier : EmptyTurretPriorityMultiplier;
            }
            else if (target is BaseTurret)
            {
                priorityMultiplier = TurretPriorityMultiplier;
            }
            else
            {
                priorityMultiplier = DefaultPriorityMultiplier;
            }

            return CalculateScoreForTarget(evaluator, target, priorityMultiplier);
        }

        private static float CalculateScoreForTarget(AIEntity evaluator, LivingEntity target, in float priorityMultiplier)
        {
            if (target is not ICharacter character) return -1f;
            else if (character.Health <= 0) return -1;

            float baseThreat = character.BaseThreatValue;
            float distanceSq = evaluator.GlobalPosition.DistanceSquaredTo(target.GlobalPosition) + 1.0f;
            float distanceFactor = 1.0f / Mathf.Pow(distanceSq, DistanceWeight * 0.5f);

            float score = baseThreat * distanceFactor * priorityMultiplier;
            float healthPercentage = character.Health / character.MaxHealth;
            float healthBonus = (1.0f - healthPercentage) * LowHealthBonusMultiplier;
            score *= 1.0f + healthBonus;

            return score;
        }
    }
}