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
        /// <returns>Словарь {AI, Позиция} или null в случае неудачи.</returns>
        public static Dictionary<AIEntity, Vector3> RequestPositionsForSquad(List<AIEntity> squad, LivingEntity target)
        {
            if (squad.Any(ai => _assignedPositions.ContainsKey(ai.GetInstanceId())))
            {
                return null; // Кто-то уже занят, отменяем.
            }

            // 1. Генерируем СПИСОК идеальных позиций.
            var idealPositions = AITacticalAnalysis.GenerateFiringArcPositions(squad, target);

            // Если не удалось сгенерировать достаточно позиций
            if (idealPositions == null || idealPositions.Count < squad.Count)
            {
                GD.Print($"Squad Coordinator: Failed to generate enough valid positions for the squad ({idealPositions?.Count ?? 0}/{squad.Count}).");
                return null;
            }

            // 2. РАСПРЕДЕЛЯЕМ эти позиции с помощью нашего умного метода.
            // Он вернет словарь, который нам нужен.
            var assignments = AITacticalAnalysis.GetOptimalAssignments(squad, idealPositions, target.GlobalPosition);

            // Если по какой-то причине распределить не удалось
            if (assignments == null || assignments.Count < squad.Count)
            {
                GD.Print($"Squad Coordinator: Failed to assign positions after generation.");
                return null;
            }

            GD.Print($"Squad Coordinator: Assigning {assignments.Count} positions for squad targeting {target.Name}.");

            // 3. Теперь мы работаем с правильным СЛОВАРЕМ. Ошибок не будет.
            // Используем деконструкцию для более чистого кода.
            foreach (var (ai, position) in assignments)
            {
                _assignedPositions[ai.GetInstanceId()] = position;
            }

            return assignments; // Возвращаем результат
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