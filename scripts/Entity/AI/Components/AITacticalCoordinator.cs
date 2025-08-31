using Godot;
using System.Collections.Generic;

namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Статический класс-координатор, который отслеживает, куда собираются двигаться
    /// AI-юниты. Это предотвращает ситуации, когда несколько юнитов пытаются занять
    /// одну и ту же позицию, вызывая "танцы" и столпотворение.
    /// </summary>
    public static class AITacticalCoordinator
    {
        private static readonly Dictionary<ulong, Vector3> _reservedDestinations = new();
        private const float RESERVATION_RADIUS_SQUARED = 2.25f; // 1.5m * 1.5m

        /// <summary>
        /// Резервирует целевую позицию для конкретного AI.
        /// </summary>
        public static void ReservePosition(AIEntity ai, Vector3 position)
        {
            _reservedDestinations[ai.GetInstanceId()] = position;
        }

        /// <summary>
        /// Снимает резервацию позиции для AI, когда он достигает цели или отменяет движение.
        /// </summary>
        public static void ReleasePosition(AIEntity ai)
        {
            _reservedDestinations.Remove(ai.GetInstanceId());
        }

        /// <summary>
        /// Проверяет, зарезервирована ли позиция (или область вокруг нее) другим AI.
        /// </summary>
        /// <param name="position">Проверяемая позиция.</param>
        /// <param name="requestingAi">AI, который запрашивает проверку (чтобы не учитывать его собственную резервацию).</param>
        /// <returns>true, если позиция занята другим AI.</returns>
        public static bool IsPositionReserved(Vector3 position, AIEntity requestingAi)
        {
            var requesterId = requestingAi.GetInstanceId();

            foreach (var entry in _reservedDestinations)
            {
                if (entry.Key == requesterId) continue;

                if (entry.Value.DistanceSquaredTo(position) < RESERVATION_RADIUS_SQUARED)
                {
                    return true;
                }
            }
            return false;
        }
    }
}