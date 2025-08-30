using System.Collections.Generic;
using Godot;
using Game.Interfaces;
using Game.Turrets;
using Game.Entity.AI.Utils;

namespace Game.Entity.AI
{
    /// <summary>
    /// Статический класс, отвечающий за логику оценки и выбора наилучшей цели для ИИ.
    /// Инкапсулирует правила приоритизации, чтобы основной класс AIEntity оставался чистым.
    /// </summary>
    public static class AITargetEvaluator
    {
        // Константы для настройки весов различных факторов при оценке угрозы.
        private const float DistanceWeight = 2f; // Насколько сильно расстояние влияет на угрозу.
        private const float TurretPriorityMultiplier = 3f; // Множитель угрозы для турелей.
        private const float LowHealthBonusMultiplier = 0.5f; // Бонус за низкое здоровье цели (0.5 = до 50% бонуса).

        /// <summary>
        /// Оценивает список потенциальных целей и возвращает наиболее приоритетную.
        /// </summary>
        /// <param name="evaluator">ИИ, который производит оценку.</param>
        /// <param name="potentialTargets">Список врагов для оценки.</param>
        /// <returns>Наиболее подходящая цель или null, если достойных целей нет.</returns>
        public static LivingEntity GetBestTarget(AIEntity evaluator, IReadOnlyList<LivingEntity> potentialTargets)
        {
            LivingEntity bestTargetObject = null;
            float highestScore = -1f;

            foreach (var potentialTarget in potentialTargets)
            {
                if (!GodotObject.IsInstanceValid(potentialTarget)) continue;

                // Определяем реальную сущность для оценки.
                // Если это игрок в турели, реальная цель - турель.
                var effectiveTarget = GetEffectiveTarget(potentialTarget, evaluator);

                // Если по какой-то причине цель недействительна (например, игрок в дружественной турели), пропускаем.
                if (effectiveTarget == null || !GodotObject.IsInstanceValid(effectiveTarget)) continue;

                float currentScore = CalculateThreatScore(evaluator, effectiveTarget);

                if (currentScore > highestScore)
                {
                    highestScore = currentScore;
                    bestTargetObject = effectiveTarget; // Сохраняем именно РЕАЛЬНУЮ цель (турель).
                }
            }
            return bestTargetObject;
        }

        /// <summary>
        /// Определяет, какую сущность на самом деле следует атаковать.
        /// Если игрок находится в турели, целью является турель.
        /// </summary>
        private static LivingEntity GetEffectiveTarget(LivingEntity potentialTarget, AIEntity evaluator)
        {
            if (potentialTarget is Player.Player player && player.IsInTurret())
            {
                var turret = player.CurrentTurret;
                // Атакуем турель, только если она враждебна.
                if (turret != null && turret.IsHostile(evaluator))
                {
                    return turret;
                }
                // Если игрок в невраждебной турели, он не является целью.
                return null;
            }
            // Для всех остальных случаев целью является сама сущность.
            return potentialTarget;
        }

        private static float CalculateThreatScore(AIEntity evaluator, LivingEntity target)
        {
            // Проверка на прямую видимость. Цель без LoS имеет 0 угрозы.
            if (!evaluator.GetVisibleTargetPoint(target).HasValue)
            {
                return -1f;
            }


            // Если это турель (независимо от того, кто в ней), у нее повышенный приоритет.
            if (target is BaseTurret)
            {
                return CalculateScoreForTarget(evaluator, target, TurretPriorityMultiplier);
            }

            // Стандартная оценка для всех остальных целей.
            return CalculateScoreForTarget(evaluator, target, 1.0f);
        }

        private static float CalculateScoreForTarget(AIEntity evaluator, PhysicsBody3D target, in float priorityMultiplier)
        {
            if (target is not ICharacter character) return -1f;
            if (character.Health <= 0) return -1f; // Добавлена проверка на живость цели

            float baseThreat = character.BaseThreatValue;

            // Фактор расстояния. Используем DistanceSquared для производительности.
            // Добавляем 1, чтобы избежать деления на ноль.
            float distanceSq = evaluator.GlobalPosition.DistanceSquaredTo(target.GlobalPosition) + 1.0f;
            float distanceFactor = 1.0f / Mathf.Pow(distanceSq, DistanceWeight * 0.5f); // 0.5f, т.к. работаем с квадратом расстояния

            float score = baseThreat * distanceFactor * priorityMultiplier;

            // Бонус за низкое здоровье. Помогает ИИ "добивать" раненых.
            float healthPercentage = character.Health / character.MaxHealth;
            float healthBonus = (1.0f - healthPercentage) * LowHealthBonusMultiplier; // Бонус до 50%
            score *= 1.0f + healthBonus;

            return score;
        }
    }
}