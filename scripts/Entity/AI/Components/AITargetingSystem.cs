using Godot;
using System.Collections.Generic;
using Game.Interfaces;

namespace Game.Entity.AI.Components
{
    public partial class AITargetingSystem : Node
    {
        [ExportGroup("Dependencies")]
        [Export] private Area3D _targetDetectionArea;

        private AIEntity _context;
        private float _targetEvaluationTimer;
        private readonly List<LivingEntity> _potentialTargets = [];

        public LivingEntity CurrentTarget { get; private set; } // Оставляем private set
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
        /// Принудительно запускает немедленную переоценку всех потенциальных целей.
        /// </summary>
        public void ForceReevaluation()
        {
            GD.Print($"{_context.Name} is forcing target re-evaluation.");
            EvaluateTargets();
            _targetEvaluationTimer = _context.Profile.CombatProfile.TargetEvaluationInterval;
        }

        /// <summary>
        /// Позволяет внешнему коду (AISquad) принудительно установить текущую цель.
        /// Используется для выполнения приказов.
        /// </summary>
        public void ForceSetCurrentTarget(LivingEntity newTarget) // <--- НОВЫЙ МЕТОД
        {
            if (newTarget == CurrentTarget) return;

            CurrentTarget = newTarget;
            if (newTarget != null)
            {
                GD.Print($"{_context.Name} received forced target: {newTarget.Name}");
            }
            else
            {
                GD.Print($"{_context.Name} target cleared by force.");
            }
        }

        private void EvaluateTargets()
        {
            _potentialTargets.RemoveAll(target => !IsInstanceValid(target) || !target.IsAlive);

            // В новой архитектуре AITargetingSystem не выбирает "лучшую" цель для атаки,
            // а лишь поддерживает список потенциальных целей для LegionBrain/AISquad.
            // CurrentTarget устанавливается только через ForceSetCurrentTarget.
            // Однако, мы можем использовать этот метод для обновления CurrentTarget
            // если текущая цель стала невалидной и есть новая, более подходящая.
            // Но в контексте LegionBrain, сам LegionBrain будет решать, кого атаковать.
            // Поэтому логика здесь упрощается.
            
            // Если CurrentTarget невалиден, очищаем его.
            if (!IsInstanceValid(CurrentTarget) || !CurrentTarget.IsAlive)
            {
                CurrentTarget = null;
            }
        }

        // Методы OnTargetEliminated, ClearTarget, SetAttackTarget больше не нужны,
        // так как их логика перенесена в LegionBrain и AISquad.
        /*
        public void OnTargetEliminated(LivingEntity eliminatedTarget) { ... }
        public void ClearTarget() { ... }
        private void SetAttackTarget(LivingEntity newTarget) { ... }
        */

        private void OnTargetDetected(Node3D body)
        {
            if (body is LivingEntity entity && body != _context)
            {
                if (_context.IsHostile(entity) && body is ICharacter && !_potentialTargets.Contains(entity))
                {
                    // GD.Print($"{_context.Name} added {body.Name} to potential targets.");
                    _potentialTargets.Add(entity);
                }
            }
        }

        private void OnTargetLost(Node3D body)
        {
            if (body is LivingEntity entity)
            {
                _potentialTargets.Remove(entity);
                // GD.Print($"{_context.Name} removed {body.Name} from potential targets.");

                // Если именно текущая цель вышла из триггера, форсируем переоценку,
                // но не меняем CurrentTarget, это делает LegionBrain.
                if (entity == CurrentTarget)
                {
                    CurrentTarget = null; // Просто очищаем, LegionBrain назначит новую.
                }
            }
        }
    }
}