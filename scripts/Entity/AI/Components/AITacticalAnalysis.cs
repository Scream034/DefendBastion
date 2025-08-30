using Godot;
using Game.Singletons;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Статический класс, содержащий "чистые" функции для тактического анализа,
    /// такие как проверка линии видимости и поиск оптимальных позиций.
    /// </summary>
    public static class AITacticalAnalysis
    {

        /// <summary>
        /// Проверяет прямую видимость до цели (LivingEntity) и возвращает позицию первой видимой точки.
        /// Использует набор предопределенных точек (SightPoints).
        /// Если точки не заданы, используется центр цели в качестве запасного варианта.
        /// </summary>
        /// <returns>Глобальная позиция первой видимой точки или null, если ни одна точка не видна.</returns>
        public static Vector3? GetFirstVisiblePoint(Node3D context, Vector3 from, LivingEntity target, Godot.Collections.Array<Rid> exclude)
        {
            if (!GodotObject.IsInstanceValid(target))
            {
                return null;
            }

            var sightPoints = target.SightPoints;

            if (sightPoints?.Length > 0)
            {
                foreach (var point in sightPoints)
                {
                    if (GodotObject.IsInstanceValid(point))
                    {
                        var pointPosition = point.GlobalPosition;
                        if (HasClearPath(context, from, pointPosition, exclude))
                        {
                            return pointPosition; // Найдена видимая точка! Возвращаем ее позицию.
                        }
                    }
                }
                return null; // Ни одна из точек не видна.
            }
            else
            {
                // Запасной вариант: проверяем центр цели.
                var targetCenter = target.GlobalPosition;
                if (HasClearPath(context, from, targetCenter, exclude))
                {
                    return targetCenter;
                }
            }

            return null;
        }

        // Старый метод оставляем для совместимости или удаляем, если он больше нигде не нужен.
        // Для чистоты я его переименовал в GetFirstVisiblePoint. Нужно будет обновить вызовы.
        public static bool HasLineOfSight(Node3D context, Vector3 from, LivingEntity target, Godot.Collections.Array<Rid> exclude)
        {
            return GetFirstVisiblePoint(context, from, target, exclude).HasValue;
        }

        /// <summary>
        /// Универсальный метод проверки прямой видимости между двумя точками.
        /// </summary>
        public static bool HasClearPath(Node3D context, Vector3 from, Vector3 to, Godot.Collections.Array<Rid> exclude = null, uint collisionMask = 1)
        {
            var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask, exclude);
            var result = Constants.DirectSpaceState.IntersectRay(query);
            return result.Count == 0;
        }

        public static Vector3? FindOptimalFiringPosition_Probing(AIEntity context, PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius)
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

            return ProbeDirections(context, searchVectors, targetPosition, weaponLocalOffset, searchRadius, navMap, exclusionList);
        }

        public static Vector3? FindOptimalFiringPosition_Hybrid(AIEntity context, PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius)
        {
            var targetPosition = target.GlobalPosition;
            var navMap = context.GetWorld3D().NavigationMap;
            var exclusionList = new Godot.Collections.Array<Rid> { context.GetRid(), target.GetRid() };

            var weaponPosition = context.GlobalPosition + (context.Basis * weaponLocalOffset);
            var spaceState = context.GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(weaponPosition, targetPosition, 1, exclusionList);
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

            return ProbeDirections(context, [.. searchVectors], targetPosition, weaponLocalOffset, searchRadius, navMap, exclusionList);
        }

        private static Vector3? ProbeDirections(AIEntity context, Vector3[] directions, Vector3 targetPosition, Vector3 weaponLocalOffset, float searchRadius, Rid navMap, Godot.Collections.Array<Rid> exclusionList)
        {
            float step = context.Profile.CombatProfile.RepositionSearchStep;
            foreach (var vector in directions.Where(v => !v.IsZeroApprox()))
            {
                for (float offset = step; offset <= searchRadius; offset += step)
                {
                    var candidatePoint = context.GlobalPosition + vector * offset;
                    var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, candidatePoint);

                    if (navMeshPoint.DistanceSquaredTo(candidatePoint) > step * step) continue;

                    var weaponPositionAtCandidate = navMeshPoint + (context.Basis * weaponLocalOffset);

                    if (HasClearPath(context, weaponPositionAtCandidate, targetPosition, exclusionList))
                    {
                        return navMeshPoint;
                    }
                }
            }
            return null;
        }
    }
}