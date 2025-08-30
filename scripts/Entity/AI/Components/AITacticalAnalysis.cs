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
        /// <summary>
        /// Анализирует прямую видимость до цели, возвращая детальную информацию о препятствиях.
        /// Это основной, оптимизированный метод для проверок LoS в бою.
        /// </summary>
        /// <param name="context">Атакующий AI.</param>
        /// <param name="from">Точка, из которой ведется проверка (дуло, глаза).</param>
        /// <param name="target">Цель.</param>
        /// <param name="collisionMask">Маска коллизий.</param>
        /// <param name="blockingAlly">Если результат BlockedByAlly, здесь будет ссылка на союзника.</param>
        /// <returns>Результат анализа линии видимости.</returns>
        public static LoSAnalysisResult AnalyzeLineOfSight(AIEntity context, Vector3 from, LivingEntity target, uint collisionMask, out AIEntity blockingAlly)
        {
            blockingAlly = null;
            var spaceState = context.GetWorld3D().DirectSpaceState;
            // Исключаем только себя, чтобы иметь возможность обнаружить ЛЮБОЕ препятствие.
            var query = PhysicsRayQueryParameters3D.Create(from, target.GlobalPosition, collisionMask, [context.GetRid()]);
            var result = spaceState.IntersectRay(query);

            if (result.Count == 0)
            {
                // GD.PrintT(context.Name, from, target.GlobalPosition, " LoSAnalysisResult.Clear");
                // Луч ни во что не попал - крайне редкая ситуация, но технически путь чист.
                return LoSAnalysisResult.Clear;
            }

            var collider = result["collider"].AsGodotObject();
            if (collider == null)
            {
                // GD.PrintT(context.Name, from, target.GlobalPosition, "LoSAnalysisResult.BlockedByObstacle");
                return LoSAnalysisResult.BlockedByObstacle; // Неопознанное препятствие.
            }

            // Если мы попали в саму цель, путь считается чистым.
            else if (collider.GetInstanceId() == target.GetInstanceId())
            {
                // GD.PrintT(context.Name, from, target.GlobalPosition, "LoSAnalysisResult.Clear2");
                return LoSAnalysisResult.Clear;
            }

            // Проверяем, является ли препятствие дружественным AI.
            else if (collider is AIEntity otherAI && !context.IsHostile(otherAI))
            {
                // GD.PrintT(context.Name, from, target.GlobalPosition, "LoSAnalysisResult.BlockedByAlly");
                blockingAlly = otherAI;
                return LoSAnalysisResult.BlockedByAlly;
            }

            // Во всех остальных случаях это статичное препятствие.
            // GD.PrintT(context.Name, from, target.GlobalPosition, "LoSAnalysisResult.BlockedByObstacle2");
            return LoSAnalysisResult.BlockedByObstacle;
        }


        /// <summary>
        /// Проверяет прямую видимость до цели (LivingEntity) и возвращает позицию первой видимой точки.
        /// Использует набор предопределенных точек (SightPoints).
        /// Если точки не заданы, используется центр цели в качестве запасного варианта.
        /// </summary>
        /// <returns>Глобальная позиция первой видимой точки или null, если ни одна точка не видна.</returns>
        public static Vector3? GetFirstVisiblePoint(Node3D context, Vector3 from, LivingEntity target, Godot.Collections.Array<Rid> exclude, uint collisionMask = 1)
        {
            if (!GodotObject.IsInstanceValid(target)) return null;

            var sightPoints = target.SightPoints;

            if (sightPoints?.Length > 0)
            {
                foreach (var point in sightPoints)
                {
                    if (GodotObject.IsInstanceValid(point) && HasClearPath(World.DirectSpaceState, from, point.GlobalPosition, exclude, collisionMask))
                    {
                        return point.GlobalPosition;
                    }
                }
                return null; // Ни одна из точек видимости не доступна
            }

            // Запасной вариант: проверяем центр цели
            var targetCenter = target.GlobalPosition;
            return HasClearPath(World.DirectSpaceState, from, targetCenter, exclude, collisionMask) ? targetCenter : null;
        }

        /// <summary>
        /// Универсальный метод проверки прямой видимости между двумя точками.
        /// </summary>
        public static bool HasClearPath(PhysicsDirectSpaceState3D spaceState, Vector3 from, Vector3 to, Godot.Collections.Array<Rid> exclude = null, uint collisionMask = 1)
        {
            var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask, exclude);
            var result = spaceState.IntersectRay(query);
            return result == null || result.Count == 0;
        }

        /// <summary>
        /// Находит подходящую позицию для короткого шага в сторону, чтобы обойти препятствие.
        /// </summary>
        public static Vector3? FindSidestepPosition(AIEntity context, LivingEntity target, float stepDistance = 2.0f, uint collisionMask = 1)
        {
            var navMap = context.GetWorld3D().NavigationMap;
            var directionToTarget = context.GlobalPosition.DirectionTo(target.GlobalPosition).Normalized();
            var sidestepVector = directionToTarget.Cross(Vector3.Up).Normalized();

            Vector3[] candidatePoints =
            [
                context.GlobalPosition + sidestepVector * stepDistance,
                context.GlobalPosition - sidestepVector * stepDistance
            ];

            var excludeList = new Godot.Collections.Array<Rid> { context.GetRid(), target.GetRid() };
            var spaceState = context.GetWorld3D().DirectSpaceState;
            var eyesOffset = context.GlobalPosition - (context.EyesPosition?.GlobalPosition ?? context.GlobalPosition);

            foreach (var point in candidatePoints)
            {
                var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, point);
                if (navMeshPoint.DistanceSquaredTo(point) < 1.0f) // Точка на навмеше достаточно близко к желаемой
                {
                    // Проверяем видимость с новой точки, учитывая смещение "глаз"
                    var fromPos = navMeshPoint - eyesOffset;
                    if (GetFirstVisiblePoint(context, fromPos, target, excludeList, collisionMask).HasValue)
                    {
                        return navMeshPoint; // Нашли подходящую точку!
                    }
                }
            }

            return null; // Не удалось найти хорошую точку для шага в сторону
        }

        public static Vector3? FindOptimalFiringPosition_Probing(AIEntity context, PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius, uint collisionMask = 1)
        {
            var targetPosition = target.GlobalPosition;
            var navMap = context.GetWorld3D().NavigationMap;
            var exclusionList = new Godot.Collections.Array<Rid> { context.GetRid(), target.GetRid() };

            var directionToTarget = context.GlobalPosition.DirectionTo(targetPosition).Normalized();
            var flankDirection = directionToTarget.Cross(Vector3.Up).Normalized();

            var searchVectors = new Vector3[]
            {
                flankDirection, -flankDirection, -directionToTarget, directionToTarget,
                (flankDirection - directionToTarget).Normalized(), (-flankDirection - directionToTarget).Normalized(),
            };

            var spaceState = context.GetWorld3D().DirectSpaceState;
            return ProbeDirections(context, spaceState, searchVectors, targetPosition, weaponLocalOffset, searchRadius, navMap, exclusionList, collisionMask);
        }

        public static Vector3? FindOptimalFiringPosition_Hybrid(AIEntity context, PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius, uint collisionMask = 1)
        {
            var targetPosition = target.GlobalPosition;
            var navMap = context.GetWorld3D().NavigationMap;
            var exclusionList = new Godot.Collections.Array<Rid> { context.GetRid(), target.GetRid() };

            var weaponPosition = context.GlobalPosition + (context.Basis * weaponLocalOffset);
            var spaceState = context.GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(weaponPosition, targetPosition, collisionMask, exclusionList);
            var result = spaceState.IntersectRay(query);

            List<Vector3> searchVectors = [];

            if (result.Count > 0)
            {
                var hitNormal = (Vector3)result["normal"];
                var surfaceTangent = hitNormal.Cross(Vector3.Up).Normalized();
                if (surfaceTangent.IsZeroApprox())
                {
                    var directionToTarget = context.GlobalPosition.DirectionTo(targetPosition).Normalized();
                    surfaceTangent = directionToTarget.Cross(hitNormal).Normalized();
                }
                searchVectors.Add(surfaceTangent);
                searchVectors.Add(-surfaceTangent);
            }

            var dirToTarget = context.GlobalPosition.DirectionTo(targetPosition).Normalized();
            var flankDir = dirToTarget.Cross(Vector3.Up).Normalized();
            searchVectors.Add(flankDir);
            searchVectors.Add(-flankDir);
            searchVectors.Add(-dirToTarget);

            return ProbeDirections(context, spaceState, [.. searchVectors], targetPosition, weaponLocalOffset, searchRadius, navMap, exclusionList, collisionMask);
        }

        private static Vector3? ProbeDirections(Node3D context, PhysicsDirectSpaceState3D spaceState, Vector3[] directions, Vector3 targetPosition, Vector3 weaponLocalOffset, float searchRadius, Rid navMap, Godot.Collections.Array<Rid> exclusionList, uint collisionMask = 1)
        {
            if (context is not AIEntity aiContext) return null;

            float step = aiContext.Profile.CombatProfile.RepositionSearchStep;
            foreach (var vector in directions.Where(v => !v.IsZeroApprox()))
            {
                for (float offset = step; offset <= searchRadius; offset += step)
                {
                    var candidatePoint = aiContext.GlobalPosition + vector * offset;
                    var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, candidatePoint);

                    if (navMeshPoint.DistanceSquaredTo(candidatePoint) > step * step) continue;

                    var weaponPositionAtCandidate = navMeshPoint + (aiContext.Basis * weaponLocalOffset);

                    if (HasClearPath(spaceState, weaponPositionAtCandidate, targetPosition, exclusionList, collisionMask))
                    {
                        return navMeshPoint;
                    }
                }
            }
            return null;
        }
    }
}