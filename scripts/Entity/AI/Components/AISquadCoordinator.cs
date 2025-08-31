using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Статический класс-планировщик, который назначает тактические позиции для группы AI.
    /// Вместо простого резервирования, он активно генерирует построения (например, огневой полукруг)
    /// и выдает каждому AI в группе конкретное место.
    /// </summary>
    public static class AISquadCoordinator
    {
        // Хранит назначенную позицию для каждого AI (GetInstanceId -> TargetPosition)
        private static readonly Dictionary<ulong, Vector3> _assignedPositions = [];

        /// <summary>
        /// Проверяет, есть ли у AI уже назначенная тактическая позиция.
        /// </summary>
        public static bool TryGetAssignedPosition(AIEntity ai, out Vector3 position)
        {
            return _assignedPositions.TryGetValue(ai.GetInstanceId(), out position);
        }

        /// <summary>
        /// Запрашивает, генерирует и назначает позиции для целой группы AI.
        /// </summary>
        /// <returns>True, если позиции были успешно назначены для всей группы.</returns>
        public static bool RequestPositionsForSquad(List<AIEntity> squad, LivingEntity target)
        {
            // Если кто-то из отряда уже выполняет тактический маневр, отменяем новый запрос.
            if (squad.Any(ai => _assignedPositions.ContainsKey(ai.GetInstanceId())))
            {
                return false;
            }

            // Генерируем идеальные позиции для построения "огневой дуги".
            var idealPositions = AITacticalAnalysis.GenerateFiringArcPositions(squad, target);

            if (idealPositions == null || idealPositions.Count != squad.Count)
            {
                GD.PrintErr("Failed to generate enough valid positions for the squad.");
                return false;
            }
            
            GD.Print($"Squad Coordinator: Assigning {idealPositions.Count} positions for squad targeting {target.Name}.");
            
            // Назначаем каждому AI его позицию.
            foreach (var assignment in idealPositions)
            {
                _assignedPositions[assignment.Key.GetInstanceId()] = assignment.Value;
            }

            return true;
        }

        /// <summary>
        /// Освобождает назначенную позицию, когда AI ее достиг или маневр отменен.
        /// </summary>
        public static void ReleasePosition(AIEntity ai)
        {
            _assignedPositions.Remove(ai.GetInstanceId());
        }
    }
}