using Game.Entity.AI.Orchestrator;
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

        /// <summary>
        /// Находит первую видимую точку на цели (из SightPoints или центра).
        /// Эта функция теперь содержит правильную логику проверки линии видимости.
        /// </summary>
        /// <param name="fromPosition">Точка, с которой ведется наблюдение.</param>
        /// <param name="target">Цель.</param>
        /// <param name="exclude">Объекты, которые нужно исключить из проверки (ОБЫЧНО ТОЛЬКО САМ СТРЕЛОК).</param>
        /// <param name="collisionMask">Маска столкновений для луча.</param>
        /// <returns>Глобальные координаты видимой точки или null, если цель не видна.</returns>
        public static Vector3? GetFirstVisiblePointOfTarget(Vector3 fromPosition, LivingEntity target, Godot.Collections.Array<Rid> exclude, uint collisionMask)
        {
            if (!GodotObject.IsInstanceValid(target)) return null;

            var pointsToCheck = new List<Vector3>();
            if (target.SightPoints?.Length > 0)
            {
                foreach (var point in target.SightPoints)
                {
                    if (GodotObject.IsInstanceValid(point))
                    {
                        pointsToCheck.Add(point.GlobalPosition);
                    }
                }
            }
            // Всегда добавляем центр цели как запасной вариант
            pointsToCheck.Add(target.GlobalPosition);

            foreach (var targetPoint in pointsToCheck)
            {
                var result = World.IntersectRay(fromPosition, targetPoint, collisionMask, exclude);

                // Если луч ни во что не попал на пути к точке, значит, путь чист.
                // Это может произойти, если точка находится немного за пределами коллайдера цели.
                if (result.Count == 0)
                {
                    return targetPoint;
                }

                var collider = result["collider"].AsGodotObject();

                // Если первое, во что попал луч, это наша цель, значит, путь до нее чист.
                if (collider != null && collider.GetInstanceId() == target.GetInstanceId())
                {
                    return targetPoint; // Нашли! Путь чист.
                }

                // Если луч попал во что-то другое, значит, эта точка не видна. Проверяем следующую.
            }

            return null; // Ни одна из точек цели не видна.
        }

        /// <summary>
        /// Находит лучшие позиции для ведения огня, предпочитая укрытия.
        /// </summary>
        public static Dictionary<AIEntity, Vector3> FindCoverAndFirePositions(List<AIEntity> squad, LivingEntity target, Vector3 muzzleOffset, int pointsToGenerate = 16)
        {
            if (squad == null || squad.Count == 0 || !GodotObject.IsInstanceValid(target)) return null;

            var validPositions = new List<Vector3>();
            var targetPos = target.GlobalPosition;
            uint losMask = squad[0].Profile.CombatProfile.LineOfSightMask;

            // <--- ИЗМЕНЕНИЕ 1: Используем МАКСИМАЛЬНУЮ дальность атаки в отряде как радиус поиска.
            // Это гарантирует, что мы не пропустим хорошие позиции для снайперов.
            float maxSquadAttackRange = squad.Max(ai => ai.CombatBehavior.AttackRange);
            float minEngagementDistanceSq = maxSquadAttackRange * 0.4f * (maxSquadAttackRange * 0.4f);

            var exclude = new Godot.Collections.Array<Rid>();
            foreach (var member in squad) { exclude.Add(member.GetRid()); }

            for (int i = 0; i < pointsToGenerate; i++)
            {
                float angle = Mathf.Pi * 2f / pointsToGenerate * i;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));

                var coverExclude = new Godot.Collections.Array<Rid>(exclude) { target.GetRid() };
                var rayEnd = targetPos + direction * maxSquadAttackRange;
                var result = World.IntersectRay(targetPos, rayEnd, losMask, coverExclude);

                if (result.Count > 0)
                {
                    var hitPosition = result["position"].AsVector3();
                    var normal = result["normal"].AsVector3();

                    for (float offset = 1.5f; offset <= 4.5f; offset += 1.5f)
                    {
                        var candidateNavMeshPoint = NavigationServer3D.MapGetClosestPoint(World.Real.NavigationMap, hitPosition + normal * offset);

                        if (candidateNavMeshPoint.DistanceSquaredTo(hitPosition + normal * offset) < 1.0f)
                        {
                            // <--- ИЗМЕНЕНИЕ 2: Добавляем строгую проверку, что позиция не дальше максимальной дальности.
                            if (candidateNavMeshPoint.DistanceSquaredTo(targetPos) < minEngagementDistanceSq ||
                                candidateNavMeshPoint.DistanceSquaredTo(targetPos) > (maxSquadAttackRange * maxSquadAttackRange))
                            {
                                continue;
                            }

                            // ... (остальная логика проверки LoS остается без изменений)
                            var directionToTarget = candidateNavMeshPoint.DirectionTo(targetPos);
                            if (directionToTarget.IsZeroApprox()) continue;
                            var lookRotation = Basis.LookingAt(directionToTarget.Normalized(), Vector3.Up);
                            var rotatedMuzzleOffset = lookRotation * muzzleOffset;
                            var checkFromPosition = candidateNavMeshPoint + rotatedMuzzleOffset;

                            if (GetFirstVisiblePointOfTarget(checkFromPosition, target, exclude, losMask).HasValue)
                            {
                                validPositions.Add(candidateNavMeshPoint);
                                break;
                            }
                        }
                    }
                }
            }

            if (validPositions.Count == 0) return null;

            // Передаем цель в GetOptimalAssignments для проверки дальности.
            return GetOptimalAssignments(squad, validPositions, target.GlobalPosition);
        }

        /// <summary>
        /// Генерирует и валидирует тактические позиции на основе ресурса Formation.
        /// </summary>
        public static Dictionary<AIEntity, Vector3> GeneratePositionsFromFormation(List<AIEntity> squad, Formation formation, LivingEntity target, Vector3 muzzleOffset)
        {
            if (formation == null || formation.MemberPositions.Length == 0 || !GodotObject.IsInstanceValid(target) || squad.Count == 0) return null;

            var squadCenter = squad.Select(m => m.GlobalPosition).Aggregate(Vector3.Zero, (a, b) => a + b) / squad.Count;
            float optimalDistance = squad.Average(ai => ai.CombatBehavior.AttackRange) * 0.8f;
            var directionFromTarget = target.GlobalPosition.DirectionTo(squadCenter).Normalized();
            var anchorPoint = target.GlobalPosition + directionFromTarget * optimalDistance;

            var formationLookDirection = anchorPoint.DirectionTo(target.GlobalPosition).Normalized();
            var formationRotation = Basis.LookingAt(formationLookDirection, Vector3.Up);

            var validCombatPositions = new List<Vector3>();
            var navMap = squad[0].GetWorld3D().NavigationMap;
            uint losMask = squad[0].Profile.CombatProfile.LineOfSightMask;

            var squadRids = new Godot.Collections.Array<Rid>();
            foreach (var member in squad) squadRids.Add(member.GetRid());

            for (int i = 0; i < formation.MemberPositions.Length; i++)
            {
                var localOffset = formation.MemberPositions[i];
                var worldOffset = formationRotation * localOffset;
                var idealPosition = anchorPoint + worldOffset;
                var navMeshPosition = NavigationServer3D.MapGetClosestPoint(navMap, idealPosition);

                if (navMeshPosition.DistanceSquaredTo(idealPosition) > 4.0f)
                {
                    continue;
                }

                // <--- КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Та же самая логика, что и в FindCoverAndFirePositions ---

                // 1. Определяем направление на цель из конкретной точки формации.
                var directionToTarget = navMeshPosition.DirectionTo(target.GlobalPosition);
                if (directionToTarget.IsZeroApprox()) continue;

                // 2. Создаем индивидуальную матрицу поворота для этой точки.
                var lookRotation = Basis.LookingAt(directionToTarget.Normalized(), Vector3.Up);

                // 3. Трансформируем смещение.
                var rotatedMuzzleOffset = lookRotation * muzzleOffset;

                // 4. Получаем точную точку для проверки.
                var checkFromPosition = navMeshPosition + rotatedMuzzleOffset;

                if (GetFirstVisiblePointOfTarget(checkFromPosition, target, squadRids, losMask).HasValue)
                {
                    validCombatPositions.Add(navMeshPosition);
                }
            }

            // Если мы не смогли найти достаточно хороших позиций для всего отряда, считаем операцию провальной.
            if (validCombatPositions.Count < squad.Count)
            {
                GD.Print($"Found only {validCombatPositions.Count} valid formation positions out of {squad.Count} required. Fallback failed.");
                return null;
            }

            return GetOptimalAssignments(squad, validCombatPositions, target.GlobalPosition);
        }

        /// <summary>
        /// Реализует жадный алгоритм для назначения агентам ближайших к ним и подходящих по дальности позиций.
        /// </summary>
        public static Dictionary<AIEntity, Vector3> GetOptimalAssignments(List<AIEntity> agents, List<Vector3> positions, Vector3 targetPosition)
        {
            var assignments = new Dictionary<AIEntity, Vector3>();
            var unassignedAgents = new List<AIEntity>(agents);
            var availablePositions = new List<Vector3>(positions);

            // Итерация по агентам, чтобы найти для каждого лучшую позицию.
            foreach (var agent in agents)
            {
                if (availablePositions.Count == 0) break;

                float agentAttackRangeSq = agent.CombatBehavior.AttackRange * agent.CombatBehavior.AttackRange;

                // Находим лучшую позицию для этого агента:
                // 1. Она должна быть в пределах его дальности атаки.
                // 2. Она должна быть ближайшей к его текущему положению.
                Vector3? bestPosition = availablePositions
                    .Where(pos => pos.DistanceSquaredTo(targetPosition) <= agentAttackRangeSq) // Фильтр по дальности
                    .OrderBy(agent.GlobalPosition.DistanceSquaredTo) // Сортировка по близости
                    .FirstOrDefault(Vector3.Zero); // Используем Zero как признак "не найдено"

                if (bestPosition.Value == Vector3.Zero && !availablePositions.Any(p => p.IsZeroApprox()))
                {
                    // Для этого агента не нашлось подходящих позиций
                    continue;
                }

                assignments[agent] = bestPosition.Value;
                availablePositions.Remove(bestPosition.Value);
            }

            return assignments;
        }

        /// <summary>
        /// Генерирует и валидирует тактическое построение "огневая дуга" для отряда,
        /// создавая точки на индивидуальных оптимальных дистанциях для каждого бойца.
        /// </summary>
        /// <returns>Список подходящих позиций. Распределением займется GetOptimalAssignments.</returns>
        public static List<Vector3> GenerateFiringArcPositions(List<AIEntity> squad, LivingEntity target)
        {
            if (squad == null || squad.Count == 0 || !GodotObject.IsInstanceValid(target)) return null;

            // 1. Определяем базовые параметры дуги
            var squadCenter = squad.Select(ai => ai.GlobalPosition).Aggregate(Vector3.Zero, (a, b) => a + b) / squad.Count;
            var directionToSquad = target.GlobalPosition.DirectionTo(squadCenter).Normalized();
            if (directionToSquad.IsZeroApprox()) directionToSquad = -target.Basis.Z; // Запасной вариант, если центр отряда совпал с целью

            int squadCount = squad.Count;
            float minSpacing = squad.Average(ai => ai.MovementController.NavigationAgent.Radius) * 2.5f;

            // Рассчитываем угол дуги, чтобы бойцы не стояли друг на друге
            // Мы возьмем среднюю дистанцию просто для расчета ширины дуги, это не критично.
            float avgOptimalDistance = squad.Average(ai => ai.CombatBehavior.AttackRange) * 0.9f;
            float requiredArcLength = minSpacing * (squadCount - 1);
            float circumference = 2 * Mathf.Pi * avgOptimalDistance;
            float totalArcRadians = (circumference > 0) ? (requiredArcLength / circumference * 2 * Mathf.Pi) : 0;
            totalArcRadians = Mathf.Min(totalArcRadians, Mathf.DegToRad(120f)); // Ограничиваем ширину

            float angleStep = squadCount > 1 ? totalArcRadians / (squadCount - 1) : 0;
            float startAngle = -totalArcRadians / 2f;

            var validPositions = new List<Vector3>();
            var navMap = squad[0].GetWorld3D().NavigationMap;
            uint losMask = squad[0].Profile.CombatProfile.LineOfSightMask;
            var exclude = new Godot.Collections.Array<Rid>(); // Исключаем только самих бойцов при проверке
            foreach (var member in squad) { exclude.Add(member.GetRid()); }

            // 2. Генерируем точки для КАЖДОГО бойца на ЕГО оптимальной дистанции
            for (int i = 0; i < squadCount; i++)
            {
                var member = squad[i]; // Берем конкретного бойца
                float memberOptimalDistance = member.CombatBehavior.AttackRange * 0.9f; // Его личная дистанция

                float currentAngle = startAngle + i * angleStep;
                var rotatedDirection = directionToSquad.Rotated(Vector3.Up, currentAngle);
                var idealPoint = target.GlobalPosition + rotatedDirection * memberOptimalDistance;

                var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, idealPoint);

                // Проверяем, что точка на навмеше и с нее видно цель
                if (navMeshPoint.DistanceSquaredTo(idealPoint) < 9f) // Увеличим допуск до 3м
                {
                    if (GetFirstVisiblePointOfTarget(navMeshPoint, target, exclude, losMask).HasValue)
                    {
                        validPositions.Add(navMeshPoint);
                    }
                }
            }

            // Метод больше не возвращает словарь, а просто список валидных точек.
            // Это делает его более универсальным. Распределением займется AISquad.
            return validPositions;
        }
    }
}