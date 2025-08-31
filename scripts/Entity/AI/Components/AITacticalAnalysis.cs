using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Представляет результат анализа линии видимости (Line of Sight).
    /// </summary>
    public enum LoSAnalysisResult
    {
        /// <summary>
        /// Путь до цели чист.
        /// </summary>
        Clear,
        /// <summary>
        /// Путь заблокирован дружественным юнитом.
        /// </summary>
        BlockedByAlly,
        /// <summary>
        /// Путь заблокирован статичным препятствием (стена, объект окружения).
        /// </summary>
        BlockedByObstacle
    }

    /// <summary>
    /// Статический класс, содержащий "чистые" функции для тактического анализа,
    /// такие как проверка линии видимости и поиск оптимальных позиций.
    /// </summary>
    public static class AITacticalAnalysis
    {
        public static LoSAnalysisResult AnalyzeLineOfSight(AIEntity context, Vector3 from, LivingEntity target, uint collisionMask, out AIEntity blockingAlly)
        {
            blockingAlly = null;
            var result = World.IntersectRay(from, target.GlobalPosition, collisionMask, [context.GetRid()]);

            if (result.Count == 0)
            {
                return LoSAnalysisResult.Clear;
            }

            var collider = result["collider"].AsGodotObject();

            if (collider == null)
            {
                return LoSAnalysisResult.BlockedByObstacle;
            }
            if (collider.GetInstanceId() == target.GetInstanceId())
            {
                return LoSAnalysisResult.Clear;
            }
            if (collider is AIEntity otherAI && !context.IsHostile(otherAI))
            {
                blockingAlly = otherAI;
                return LoSAnalysisResult.BlockedByAlly;
            }

            return LoSAnalysisResult.BlockedByObstacle;
        }

        public static Vector3? GetFirstVisiblePoint(Vector3 from, LivingEntity target, Godot.Collections.Array<Rid> exclude, uint collisionMask = 1)
        {
            if (!GodotObject.IsInstanceValid(target)) return null;

            if (target.SightPoints?.Length > 0)
            {
                foreach (var point in target.SightPoints)
                {
                    if (GodotObject.IsInstanceValid(point) && HasClearPath(from, point.GlobalPosition, exclude, collisionMask))
                    {
                        return point.GlobalPosition;
                    }
                }
                return null;
            }

            var targetCenter = target.GlobalPosition;
            return HasClearPath(from, targetCenter, exclude, collisionMask) ? targetCenter : null;
        }

        public static bool HasClearPath(Vector3 from, Vector3 to, Godot.Collections.Array<Rid> exclude = null, uint collisionMask = 1)
        {
            var result = World.IntersectRay(from, to, collisionMask, exclude);
            return result.Count == 0;
        }

        /// <summary>
        /// Находит первую видимую точку на цели (из SightPoints или центра).
        /// </summary>
        /// <param name="fromPosition">Точка, с которой ведется наблюдение.</param>
        /// <param name="target">Цель.</param>
        /// <param name="exclude">Объекты, которые нужно исключить из проверки (обычно сам стрелок и цель).</param>
        /// <param name="collisionMask">Маска столкновений для луча.</param>
        /// <returns>Глобальные координаты видимой точки или null, если цель не видна.</returns>
        public static Vector3? GetFirstVisiblePointOfTarget(Vector3 fromPosition, LivingEntity target, Godot.Collections.Array<Rid> exclude, uint collisionMask)
        {
            if (!GodotObject.IsInstanceValid(target)) return null;

            if (target.SightPoints?.Length > 0)
            {
                foreach (var point in target.SightPoints)
                {
                    if (GodotObject.IsInstanceValid(point) && HasClearPath(fromPosition, point.GlobalPosition, exclude, collisionMask))
                    {
                        return point.GlobalPosition; // Нашли! Возвращаем конкретную точку.
                    }
                }
            }

            // Fallback: если уязвимых точек нет или ни одна не видна, проверяем центр объекта.
            var targetCenter = target.GlobalPosition;
            if (HasClearPath(fromPosition, targetCenter, exclude, collisionMask))
            {
                return targetCenter;
            }

            return null; // Цель не видна.
        }

        /// <summary>
        /// Находит лучшие позиции для ведения огня, предпочитая укрытия.
        /// Алгоритм работает "от цели", находя края препятствий и проверяя точки за ними,
        /// с учетом тактически верной дистанции.
        /// </summary>
        public static Dictionary<AIEntity, Vector3> FindCoverAndFirePositions(List<AIEntity> squad, LivingEntity target, int pointsToGenerate = 16)
        {
            if (squad == null || squad.Count == 0 || !GodotObject.IsInstanceValid(target)) return null;

            var validPositions = new List<Vector3>();
            var targetPos = target.GlobalPosition;
            uint losMask = squad[0].Profile.CombatProfile.LineOfSightMask;

            // Определяем тактические дистанции
            float averageAttackRange = squad.Average(ai => ai.CombatBehavior.AttackRange);
            // Минимальная дистанция, чтобы не подходить вплотную. Например, 40% от макс. дальности.
            float minEngagementDistanceSq = averageAttackRange * 0.4f * (averageAttackRange * 0.4f);
            // Максимальная дистанция для поиска укрытий, равна средней дальности атаки.
            float maxSearchRadius = averageAttackRange;

            // 1. Генерируем лучи ИЗ ЦЕЛИ во все стороны.
            for (int i = 0; i < pointsToGenerate; i++)
            {
                float angle = (Mathf.Pi * 2f / pointsToGenerate) * i;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var rayEnd = targetPos + direction * maxSearchRadius;

                // Важно: Исключаем самого себя И ЦЕЛЬ из raycast, чтобы не попасть в коллайдер цели.
                var exclude = new Godot.Collections.Array<Rid> { target.GetRid() };
                foreach (var member in squad) { exclude.Add(member.GetRid()); }

                var result = World.IntersectRay(targetPos, rayEnd, losMask, exclude);

                if (result.Count > 0)
                {
                    // 2. Мы попали в препятствие. Точка ПЕРЕД ним - это край укрытия.
                    var hitPosition = result["position"].AsVector3();
                    var normal = result["normal"].AsVector3();

                    // 3. Проверяем несколько точек ЗА этим укрытием.
                    for (float offset = 1.5f; offset <= 4.5f; offset += 1.5f)
                    {
                        var candidatePoint = hitPosition + normal * offset;
                        var navMeshPoint = NavigationServer3D.MapGetClosestPoint(World.Real.NavigationMap, candidatePoint);

                        // 4. Валидация точки.
                        if (navMeshPoint.DistanceSquaredTo(candidatePoint) < 1.0f) // Доступна на навмеше
                        {
                            // Добавляем проверку дистанции!
                            float distanceToTargetSq = navMeshPoint.DistanceSquaredTo(targetPos);
                            if (distanceToTargetSq < minEngagementDistanceSq)
                            {
                                // Это укрытие слишком близко к цели. Игнорируем.
                                continue;
                            }

                            // С нее есть прострел до цели, но от цели до нее - нет (т.е. это укрытие).
                            if (GetFirstVisiblePointOfTarget(navMeshPoint, target, exclude, losMask).HasValue)
                            {
                                validPositions.Add(navMeshPoint);
                                break;
                            }
                        }
                    }
                }
            }

            if (validPositions.Count == 0)
            {
                // Если не найдено ни одного подходящего укрытия, возвращаем null,
                // чтобы вызывающий код использовал fallback-логику (боевую формацию).
                return null;
            }

            // 5. Распределяем лучшие найденные позиции между членами отряда.
            var assignments = new Dictionary<AIEntity, Vector3>();
            var unassignedAIs = new List<AIEntity>(squad);

            // Сортируем позиции, чтобы дать приоритет тем, что дальше от цели (безопаснее)
            var sortedPositions = validPositions.OrderByDescending(p => p.DistanceSquaredTo(targetPos));

            foreach (var pos in sortedPositions)
            {
                if (unassignedAIs.Count == 0) break;

                // Находим AI, для которого эта позиция наиболее близка к его текущему положению.
                var bestAI = unassignedAIs.OrderBy(ai => ai.GlobalPosition.DistanceSquaredTo(pos)).First();
                assignments[bestAI] = pos;
                unassignedAIs.Remove(bestAI);
            }

            // Если для кого-то не хватило укрытий, им позиция не назначается.
            // В идеале, нужно иметь логику для тех, кто остался без приказа (например, держать позицию).
            // Но в нашей системе они просто не получат нового приказа MoveTo, что тоже приемлемо.
            return assignments;
        }

        /// <summary>
        /// Генерирует и валидирует тактическое построение "огневая дуга" для отряда.
        /// </summary>
        /// <returns>Словарь {AI, Позиция} или null, если построение невозможно.</returns>
        public static Dictionary<AIEntity, Vector3> GenerateFiringArcPositions(List<AIEntity> squad, LivingEntity target)
        {
            if (squad == null || squad.Count == 0 || !GodotObject.IsInstanceValid(target)) return null;

            // Учитываем радиус юнитов для минимального расстояния
            float minSpacing = squad.Average(ai => ai.MovementController.NavigationAgent.Radius) * 2.5f; // Расстояние = 2.5 радиуса

            // Определяем параметры дуги
            var squadCenter = squad.Select(ai => ai.GlobalPosition).Aggregate(Vector3.Zero, (a, b) => a + b) / squad.Count;
            var directionToSquad = target.GlobalPosition.DirectionTo(squadCenter).Normalized();

            float optimalDistance = squad.Average(ai => ai.CombatBehavior.AttackRange) * 0.9f;
            int squadCount = squad.Count;

            // Динамически вычисляем угол, чтобы обеспечить минимальное расстояние
            float requiredArcLength = minSpacing * (squadCount - 1);
            float circumferenceAtOptimalDistance = 2 * Mathf.Pi * optimalDistance;
            float totalArcRadians = requiredArcLength / circumferenceAtOptimalDistance * 2 * Mathf.Pi;
            totalArcRadians = Mathf.Min(totalArcRadians, Mathf.DegToRad(120f)); // Ограничиваем максимальную ширину дуги

            float angleStep = squadCount > 1 ? totalArcRadians / (squadCount - 1) : 0;
            float startAngle = -totalArcRadians / 2f;

            var assignments = new Dictionary<AIEntity, Vector3>();
            var availablePositions = new List<Vector3>();

            // 2. Генерируем "идеальные" точки на дуге
            for (int i = 0; i < squadCount; i++)
            {
                float currentAngle = startAngle + i * angleStep;
                var rotatedDirection = directionToSquad.Rotated(Vector3.Up, currentAngle);
                var idealPoint = target.GlobalPosition + rotatedDirection * optimalDistance;

                var navMap = squad[0].GetWorld3D().NavigationMap;
                var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, idealPoint);

                if (navMeshPoint.DistanceSquaredTo(idealPoint) < 4f)
                {
                    uint mask = squad[0].Profile.CombatProfile.LineOfSightMask;
                    if (GetFirstVisiblePointOfTarget(navMeshPoint, target, [target.GetRid()], mask).HasValue)
                    {
                        availablePositions.Add(navMeshPoint);
                    }
                }
            }

            // ВАЖНО: Распределение позиций теперь происходит в AISquad,
            // поэтому здесь мы просто возвращаем null, если позиций не хватает.
            // Но для `GenerateFiringArcPositions`, который используется как часть `FindCover...`
            // лучше оставить старую логику, так как она может вызываться отдельно.
            // Для чистоты архитектуры мы можем делегировать распределение `AISquad`,
            // но пока оставим так для совместимости.

            if (availablePositions.Count < squadCount) return null;

            // Мы можем использовать тот же GetOptimalAssignments, если бы он был статическим,
            // но пока оставим простое распределение внутри AITacticalAnalysis.
            var unassignedAIs = new List<AIEntity>(squad);
            foreach (var pos in availablePositions)
            {
                if (unassignedAIs.Count == 0) break;

                var bestAI = unassignedAIs.OrderBy(ai => ai.GlobalPosition.DistanceSquaredTo(pos)).First();
                assignments[bestAI] = pos;
                unassignedAIs.Remove(bestAI);
            }

            return assignments;
        }
    }
}