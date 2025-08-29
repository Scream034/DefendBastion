using System.Collections.Generic;
using Godot;
using Game.Interfaces;
using Game.Turrets;
using Game.Entity.Components.Resources;
using System.Runtime.CompilerServices;

namespace Game.Entity.AI
{
    /// <summary>
    /// Статический класс, отвечающий за логику оценки и выбора наилучшей цели для ИИ.
    /// Инкапсулирует правила приоритизации, чтобы основной класс AIEntity оставался чистым.
    /// </summary>
    public static class AITargetEvaluator
    {
        // Константы для настройки весов различных факторов при оценке угрозы.
        private const float DistanceWeight = 1.5f; // Насколько сильно расстояние влияет на угрозу.
        private const float TurretPriorityMultiplier = 2.5f; // Множитель угрозы для турелей.
        private const float LowHealthBonusMultiplier = 0.5f; // Бонус за низкое здоровье цели (0.5 = до 50% бонуса).

        /// <summary>
        /// Оценивает список потенциальных целей и возвращает наиболее приоритетную.
        /// </summary>
        /// <param name="evaluator">ИИ, который производит оценку.</param>
        /// <param name="potentialTargets">Список врагов для оценки.</param>
        /// <returns>Наиболее подходящая цель или null, если достойных целей нет.</returns>
        public static PhysicsBody3D GetBestTarget(AIEntity evaluator, List<PhysicsBody3D> potentialTargets)
        {
            PhysicsBody3D bestTarget = null;
            float highestScore = -1f;

            foreach (var potentialTarget in potentialTargets)
            {
                // Пропускаем цели, которые уже не валидны.
                if (!GodotObject.IsInstanceValid(potentialTarget)) continue;

                float currentScore = CalculateThreatScore(evaluator, potentialTarget);

                if (currentScore > highestScore)
                {
                    highestScore = currentScore;
                    bestTarget = potentialTarget;
                }
            }
            return bestTarget;
        }

        private static float CalculateThreatScore(AIEntity evaluator, PhysicsBody3D target)
        {
            // Проверка на прямую видимость. Цель без LoS имеет 0 угрозы.
            if (!evaluator.HasLineOfSightTo(target))
            {
                return -1f;
            }

            // 2Специальная логика для игрока в турели.
            else if (target is Player.Player player && player.IsInTurret())
            {
                var turret = player.CurrentTurret;
                if (turret != null && GodotObject.IsInstanceValid(turret) && turret.IsHostile(evaluator))
                {
                    // Если игрок в турели, мы перенаправляем оценку на саму турель.
                    // Угроза от самого игрока в этот момент минимальна.
                    return CalculateScoreForTarget(evaluator, turret, TurretPriorityMultiplier);
                }
                else
                {
                    // Игрок в турели, но турель по какой-то причине не является целью.
                    // В этом случае игрок почти не представляет угрозы.
                    return 0.1f;
                }
            }

            // Если это турель, в которой сидит игрок, мы уже обработали ее выше. 
            // Но если это автономная турель, или в ней сидит вражеский AI, ее нужно оценить.
            if (target is ControllableTurret controlledTurret && controlledTurret.CurrentController != null)
            {
                // Если в турели сидит враг, это ОЧЕНЬ высокая угроза.
                return CalculateScoreForTarget(evaluator, controlledTurret, TurretPriorityMultiplier);
            }

            // 3. Стандартная оценка для всех остальных целей.
            return CalculateScoreForTarget(evaluator, target, 1.0f);
        }

        private static float CalculateScoreForTarget(AIEntity evaluator, PhysicsBody3D target, float priorityMultiplier)
        {
            if (target is not ICharacter character) return -1f;

            // Получаем компонент CharacterStats цели, если он есть.
            float baseThreat = character.Stats.BaseThreatValue; // Значение по умолчанию, если статов нет.

            // 4. Фактор расстояния. Используем DistanceSquared для производительности.
            // Добавляем 1, чтобы избежать деления на ноль.
            float distanceSq = evaluator.GlobalPosition.DistanceSquaredTo(target.GlobalPosition) + 1.0f;
            float distanceFactor = 1.0f / Mathf.Pow(distanceSq, DistanceWeight * 0.5f); // 0.5f, т.к. работаем с квадратом расстояния

            float score = baseThreat * distanceFactor * priorityMultiplier;

            // 5. Бонус за низкое здоровье. Помогает ИИ "добивать" раненых.
            if (target is ICharacter damageable && damageable.Stats.MaxHealth > 0)
            {
                float healthPercentage = damageable.Stats.Health / damageable.Stats.MaxHealth;
                float healthBonus = (1.0f - healthPercentage) * LowHealthBonusMultiplier; // Бонус до 50%
                score *= 1.0f + healthBonus;
            }

            return score;
        }
    }
}