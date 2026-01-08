using Godot;
using System.Collections.Generic;
using Game.Interfaces;
namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Система обнаружения целей на основе Area3D.
    /// Отвечает за список потенциальных целей в радиусе слышимости/видимости сенсора.
    /// </summary>
    public partial class AITargetingSystem : Node
    {
        [ExportGroup("Dependencies")]
        [Export] private Area3D _targetDetectionArea;
        private AIEntity _context;
        private float _targetEvaluationTimer;
        private readonly List<LivingEntity> _potentialTargets = [];

        /// <summary>
        /// Текущая приоритетная цель, назначенная логикой (Squad/Brain).
        /// Сама система не выбирает цель автоматически, только предоставляет список.
        /// </summary>
        public LivingEntity CurrentTarget { get; private set; }

        /// <summary>
        /// Список всех живых целей, находящихся внутри зоны обнаружения.
        /// </summary>
        public IReadOnlyList<LivingEntity> PotentialTargets => _potentialTargets;

        public void Initialize(AIEntity context)
        {
            _context = context;
            if (_context.Profile?.CombatProfile == null)
            {
                GD.PushError($"AICombatProfile is not set for {_context.Name}. Targeting system will not work.");
                SetProcess(false);
                return;
            }

            _targetEvaluationTimer = _context.Profile.CombatProfile.TargetEvaluationInterval;

            if (_targetDetectionArea == null)
            {
                GD.PushError($"TargetDetectionArea not assigned to {Name}!");
                SetProcess(false);
                return;
            }

            // Подписываемся на физические события входа/выхода из зоны
            _targetDetectionArea.BodyEntered += OnTargetDetected;
            _targetDetectionArea.BodyExited += OnTargetLost;
        }

        public override void _PhysicsProcess(double delta)
        {
            _targetEvaluationTimer -= (float)delta;
            if (_targetEvaluationTimer <= 0f)
            {
                EvaluateTargets();
                _targetEvaluationTimer = _context.Profile.CombatProfile.TargetEvaluationInterval;
            }
        }

        /// <summary>
        /// Проверяет, находится ли указанная цель физически внутри коллайдера зоны обнаружения.
        /// </summary>
        /// <param name="target">Цель для проверки.</param>
        /// <returns>True, если цель внутри Area3D.</returns>
        public bool IsTargetInSensorRange(LivingEntity target)
        {
            if (!IsInstanceValid(target)) return false;
            // Используем Contains, так как список поддерживается в актуальном состоянии через сигналы Area3D
            return _potentialTargets.Contains(target);
        }

        /// <summary>
        /// Принудительно запускает очистку списка от мертвых целей.
        /// </summary>
        public void ForceReevaluation()
        {
            EvaluateTargets();
            _targetEvaluationTimer = _context.Profile.CombatProfile.TargetEvaluationInterval;
        }

        /// <summary>
        /// Принудительно устанавливает текущую цель (например, по приказу Сквада).
        /// </summary>
        public void ForceSetCurrentTarget(LivingEntity newTarget)
        {
            if (newTarget == CurrentTarget) return;
            CurrentTarget = newTarget;
        }

        private void EvaluateTargets()
        {
            // Удаляем уничтоженные объекты из списка (оптимизация: for reversed loop)
            for (int i = _potentialTargets.Count - 1; i >= 0; i--)
            {
                var target = _potentialTargets[i];
                if (!IsInstanceValid(target) || !target.IsAlive)
                {
                    _potentialTargets.RemoveAt(i);
                }
            }

            // Если текущая цель стала невалидной, сбрасываем ссылку
            if (!IsInstanceValid(CurrentTarget) || !CurrentTarget.IsAlive)
            {
                CurrentTarget = null;
            }
        }

        private void OnTargetDetected(Node3D body)
        {
            if (body is LivingEntity entity && body != _context)
            {
                // Добавляем только враждебные цели, которые являются персонажами
                if (_context.IsHostile(entity) && body is ICharacter && !_potentialTargets.Contains(entity))
                {
                    _potentialTargets.Add(entity);
                }
            }
        }

        private void OnTargetLost(Node3D body)
        {
            if (body is LivingEntity entity)
            {
                _potentialTargets.Remove(entity);
                // Примечание: Мы НЕ сбрасываем CurrentTarget здесь.
                // Это задача CombatState/PursuitState решать, что делать при потере цели.
            }
        }
    }
}