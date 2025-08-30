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

        public static Vector3? FindSidestepPosition(AIEntity context, LivingEntity target)
        {
            var directionToTarget = context.GlobalPosition.DirectionTo(target.GlobalPosition).Normalized();
            var sidestepVector = directionToTarget.Cross(Vector3.Up).Normalized();
            uint losMask = context.Profile.CombatProfile.LineOfSightMask;

            // Проверяем несколько точек вбок на разном расстоянии для большей гибкости
            float[] stepDistances = [1.5f, 2.5f, 1.0f];

            // Определяем, с какой стороны от нас находится ближайшая стена, чтобы не шагать в нее
            bool isLeftBlocked = World.IntersectRay(context.GlobalPosition, context.GlobalPosition - sidestepVector * 2f, losMask, [context.GetRid()]).Count > 0;
            bool isRightBlocked = World.IntersectRay(context.GlobalPosition, context.GlobalPosition + sidestepVector * 2f, losMask, [context.GetRid()]).Count > 0;

            var directions = new List<Vector3>();
            if (!isRightBlocked) directions.Add(sidestepVector);   // Предпочитаем шаг вправо
            if (!isLeftBlocked) directions.Add(-sidestepVector); // Затем влево

            var weaponLocalOffset = context.CombatBehavior is StationaryCombatBehavior scb && scb.Action.MuzzlePoint != null ? scb.Action.MuzzlePoint.Position : Vector3.Zero;

            foreach (var dir in directions)
            {
                foreach (var dist in stepDistances)
                {
                    var point = context.GlobalPosition + dir * dist;
                    var navMeshPoint = NavigationServer3D.MapGetClosestPoint(World.Real.NavigationMap, point);
                    // Убеждаемся, что точка на навмеше очень близко к нашей боковой цели
                    if (navMeshPoint.DistanceSquaredTo(point) < 0.5f)
                    {
                        var fromPos = navMeshPoint + (context.Basis * weaponLocalOffset);
                        if (AnalyzeLineOfSight(context, fromPos, target, losMask, out _) == LoSAnalysisResult.Clear)
                        {
                            return navMeshPoint;
                        }
                    }
                }
            }

            return null; // Не удалось найти хорошую точку
        }

        public static Vector3? FindOptimalFiringPosition_Probing(AIEntity context, PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius)
        {
            var directionToTarget = context.GlobalPosition.DirectionTo(target.GlobalPosition).Normalized();
            var flankDirection = directionToTarget.Cross(Vector3.Up).Normalized();

            var searchVectors = new Vector3[]
            {
                flankDirection, -flankDirection, -directionToTarget, directionToTarget,
                (flankDirection - directionToTarget).Normalized(), (-flankDirection - directionToTarget).Normalized(),
            };

            return ProbeDirections(context, target.GlobalPosition, searchVectors, weaponLocalOffset, searchRadius);
        }

        public static Vector3? FindOptimalFiringPosition_Hybrid(AIEntity context, PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius)
        {
            var targetPosition = target.GlobalPosition;
            var weaponPosition = context.GlobalPosition + (context.Basis * weaponLocalOffset);
            var result = World.IntersectRay(weaponPosition, targetPosition, context.Profile.CombatProfile.LineOfSightMask, [context.GetRid(), target.GetRid()]);

            var searchVectors = new List<Vector3>();

            if (result.Count > 0)
            {
                var hitNormal = (Vector3)result["normal"];
                var surfaceTangent = hitNormal.Cross(Vector3.Up).Normalized();
                if (surfaceTangent.IsZeroApprox())
                {
                    surfaceTangent = context.GlobalPosition.DirectionTo(targetPosition).Normalized().Cross(hitNormal).Normalized();
                }
                searchVectors.Add(surfaceTangent);
                searchVectors.Add(-surfaceTangent);
            }

            var dirToTarget = context.GlobalPosition.DirectionTo(targetPosition).Normalized();
            var flankDir = dirToTarget.Cross(Vector3.Up).Normalized();
            searchVectors.Add(flankDir);
            searchVectors.Add(-flankDir);
            searchVectors.Add(-dirToTarget);

            return ProbeDirections(context, targetPosition, [.. searchVectors], weaponLocalOffset, searchRadius);
        }

        private static Vector3? ProbeDirections(AIEntity context, Vector3 targetPosition, Vector3[] directions, Vector3 weaponLocalOffset, float searchRadius)
        {
            float step = context.Profile.CombatProfile.RepositionSearchStep;
            uint collisionMask = context.Profile.CombatProfile.LineOfSightMask;

            foreach (var vector in directions.Where(v => !v.IsZeroApprox()))
            {
                for (float offset = step; offset <= searchRadius; offset += step)
                {
                    var candidatePoint = context.GlobalPosition + vector * offset;
                    var navMeshPoint = NavigationServer3D.MapGetClosestPoint(World.Real.NavigationMap, candidatePoint);

                    if (navMeshPoint.DistanceSquaredTo(candidatePoint) > step * step) continue;

                    var weaponPositionAtCandidate = navMeshPoint + (context.Basis * weaponLocalOffset);
                    if (HasClearPath(weaponPositionAtCandidate, targetPosition, [context.GetRid()], collisionMask))
                    {
                        return navMeshPoint;
                    }
                }
            }
            return null;
        }
    }
}