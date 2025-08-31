using Game.Entity.AI.Behaviors;
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
        /// Находит наилучшую тактическую позицию для атаки цели.
        /// Генерирует набор точек-кандидатов, оценивает их и возвращает лучшую.
        /// </summary>
        public static Vector3? FindBestTacticalPosition(AIEntity context, LivingEntity target, Vector3 weaponLocalOffset, float searchRadius, bool preferSidestep)
        {
            var candidates = new List<(Vector3 Position, float Score)>();
            var directionToTarget = context.GlobalPosition.DirectionTo(target.GlobalPosition).Normalized();
            var flankDirection = directionToTarget.Cross(Vector3.Up).Normalized();

            // Генерируем направления для поиска: сначала боковые, потом остальные.
            var searchVectors = new List<Vector3>
            {
                flankDirection,         // Вправо
                -flankDirection,        // Влево
                (flankDirection - directionToTarget).Normalized(),  // Вправо-назад
                (-flankDirection - directionToTarget).Normalized(), // Влево-назад
                -directionToTarget      // Прямо назад
            };

            // Если не требуется боковой шаг, добавляем и другие направления
            if (!preferSidestep)
            {
                searchVectors.Add(directionToTarget); // Прямо вперед
                searchVectors.Add((flankDirection + directionToTarget).Normalized()); // Вправо-вперед
                searchVectors.Add((-flankDirection + directionToTarget).Normalized()); // Влево-вперед
            }

            float step = context.Profile.CombatProfile.RepositionSearchStep;
            uint collisionMask = context.Profile.CombatProfile.LineOfSightMask;

            foreach (var vector in searchVectors.Where(v => !v.IsZeroApprox()))
            {
                // Проверяем точки на разном удалении в каждом направлении
                for (float offset = step; offset <= searchRadius; offset += step)
                {
                    var candidatePoint = context.GlobalPosition + vector * offset;
                    var navMeshPoint = NavigationServer3D.MapGetClosestPoint(World.Real.NavigationMap, candidatePoint);

                    // Проверяем, что точка на навмеше достаточно близка к нашей цели
                    if (navMeshPoint.DistanceSquaredTo(candidatePoint) > step * step) continue;

                    // Проверяем, не зарезервирована ли эта точка другим союзником
                    if (AITacticalCoordinator.IsPositionReserved(navMeshPoint, context)) continue;

                    var weaponPositionAtCandidate = navMeshPoint + (context.Basis * weaponLocalOffset);
                    if (HasClearPath(weaponPositionAtCandidate, target.GlobalPosition, [context.GetRid(), target.GetRid()], collisionMask))
                    {
                        // Оцениваем позицию. Приоритет у более близких к нам точек.
                        float score = 1.0f / (1.0f + context.GlobalPosition.DistanceSquaredTo(navMeshPoint));
                        candidates.Add((navMeshPoint, score));
                        // Нашли хорошую точку в этом направлении, можно переходить к следующему вектору.
                        break;
                    }
                }
            }

            if (candidates.Count == 0) return null;

            // Возвращаем кандидата с наивысшим счетом
            return candidates.OrderByDescending(c => c.Score).First().Position;
        }

        /// <summary>
        /// Генерирует и валидирует тактическое построение "огневая дуга" для отряда.
        /// </summary>
        /// <returns>Словарь {AI, Позиция} или null, если построение невозможно.</returns>
        public static Dictionary<AIEntity, Vector3> GenerateFiringArcPositions(List<AIEntity> squad, LivingEntity target)
        {
            if (squad == null || squad.Count == 0 || !GodotObject.IsInstanceValid(target)) return null;

            // 1. Определяем параметры дуги
            var squadCenter = squad.Select(ai => ai.GlobalPosition).Aggregate(Vector3.Zero, (a, b) => a + b) / squad.Count;
            var directionToSquad = target.GlobalPosition.DirectionTo(squadCenter).Normalized();

            // Используем среднюю дальность атаки отряда как радиус дуги
            float optimalDistance = squad.Average(ai => ai.CombatBehavior.AttackRange) * 0.9f;
            int squadCount = squad.Count;
            float totalArcDegrees = Mathf.Min(squadCount * 20f, 120f); // Ширина дуги, например 20 градусов на юнита
            float angleStep = squadCount > 1 ? Mathf.DegToRad(totalArcDegrees) / (squadCount - 1) : 0;
            float startAngle = -Mathf.DegToRad(totalArcDegrees) / 2f;

            var assignments = new Dictionary<AIEntity, Vector3>();
            var availablePositions = new List<Vector3>();

            // 2. Генерируем "идеальные" точки на дуге
            for (int i = 0; i < squadCount; i++)
            {
                float currentAngle = startAngle + i * angleStep;
                var rotatedDirection = directionToSquad.Rotated(Vector3.Up, currentAngle);
                var idealPoint = target.GlobalPosition + rotatedDirection * optimalDistance;

                // 3. Валидируем каждую точку
                var navMap = squad[0].GetWorld3D().NavigationMap;
                var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, idealPoint);

                // Точка валидна, если она на навмеше, близко к идеальной, и с нее есть прострел
                if (navMeshPoint.DistanceSquaredTo(idealPoint) < 4f) // Допуск ~2 метра
                {
                    uint mask = squad[0].Profile.CombatProfile.LineOfSightMask;
                    if (HasClearPath(navMeshPoint, target.GlobalPosition, [target.GetRid()], mask))
                    {
                        availablePositions.Add(navMeshPoint);
                    }
                }
            }

            if (availablePositions.Count < squadCount) return null; // Недостаточно хороших позиций для всего отряда

            // 4. Распределяем ближайшие валидные точки между членами отряда
            var unassignedAIs = new List<AIEntity>(squad);
            foreach (var pos in availablePositions)
            {
                if (unassignedAIs.Count == 0) break;

                // Находим ближайшего к этой точке свободного AI
                var bestAI = unassignedAIs.OrderBy(ai => ai.GlobalPosition.DistanceSquaredTo(pos)).First();
                assignments[bestAI] = pos;
                unassignedAIs.Remove(bestAI);
            }

            return assignments;
        }
    }
}