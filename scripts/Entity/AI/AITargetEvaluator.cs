using System.Collections.Generic;
using Godot;
using Game.Interfaces;
using Game.Turrets;

namespace Game.Entity.AI
{
    public static class AITargetEvaluator
    {
        // Новые константы для гибкой настройки приоритетов
        private const float DistanceWeight = 2f;
        private const float LowHealthBonusMultiplier = 0.5f;

        /// <summary>Множитель для турелей, в которых находится враг. Самый высокий приоритет.</summary>
        private const float OccupiedTurretPriorityMultiplier = 3.0f;
        /// <summary>Множитель для обычных AI турелей (неуправляемых игроком).</summary>
        private const float TurretPriorityMultiplier = 1.5f;
        /// <summary>Множитель для пустых турелей. Низкий приоритет, чтобы AI не атаковал их, если есть цели важнее.</summary>
        private const float EmptyTurretPriorityMultiplier = 0.5f;
        /// <summary>Стандартный множитель для игрока и других сущностей.</summary>
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

                float currentScore = CalculateThreatScore(evaluator, effectiveTarget);
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

        private static float CalculateThreatScore(AIEntity evaluator, LivingEntity target)
        {
            if (!evaluator.GetVisibleTargetPoint(target).HasValue)
            {
                return -1f;
            }

            float priorityMultiplier;

            // Определяем приоритет в зависимости от типа и состояния цели
            if (target is ControllableTurret cTurret)
            {
                // Если турель занята - это угроза №1. Если пуста - низкий приоритет.
                priorityMultiplier = cTurret.CurrentController != null ? OccupiedTurretPriorityMultiplier : EmptyTurretPriorityMultiplier;
            }
            else if (target is BaseTurret)
            {
                // Это AI турель, у нее средний приоритет.
                priorityMultiplier = TurretPriorityMultiplier;
            }
            else
            {
                // Это игрок или другой моб. Стандартный приоритет.
                priorityMultiplier = DefaultPriorityMultiplier;
            }

            return CalculateScoreForTarget(evaluator, target, priorityMultiplier);
        }

        private static float CalculateScoreForTarget(AIEntity evaluator, LivingEntity target, in float priorityMultiplier)
        {
            if (target is not ICharacter character) return -1f;
            else if (character.Health < 0) return -1;

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