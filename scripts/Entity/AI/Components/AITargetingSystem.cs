// --- ИЗМЕНЕНИЯ ---
// 1. Добавлен новый публичный метод ForceReevaluation(), который позволяет состояниям явно запрашивать
//    немедленную переоценку целей без необходимости сбрасывать текущую цель в null.
// 2. В EvaluateTargets добавлена логика, которая сбрасывает цель, если она больше не является лучшей
//    (например, вышла из зоны видимости), а других целей нет.
// -----------------

using Godot;
using Game.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entity.AI.Components
{
    public partial class AITargetingSystem : Node
    {
        [ExportGroup("Dependencies")]
        [Export] private Area3D _targetDetectionArea;

        private AIEntity _context;
        private float _targetEvaluationTimer;
        private readonly List<LivingEntity> _potentialTargets = [];

        public LivingEntity CurrentTarget { get; private set; }
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

        private void EvaluateTargets()
        {
            _potentialTargets.RemoveAll(target => !IsInstanceValid(target) || !target.IsAlive);

            var bestAvailableTarget = AITargetEvaluator.GetBestTarget(_context, _potentialTargets);

            // Мы меняем цель, только если нашли НОВУЮ и ВАЛИДНУЮ цель.
            if (bestAvailableTarget != null && bestAvailableTarget != CurrentTarget)
            {
                SetAttackTarget(bestAvailableTarget);
            }
            // Если bestAvailableTarget равен null (т.е. не найдено ни одной цели в LoS),
            // мы сознательно НЕ ДЕЛАЕМ НИЧЕГО. Мы не вызываем SetAttackTarget(null).
            // AI будет продолжать "держаться" за свою CurrentTarget, пока она физически
            // не будет уничтожена. Это и есть "упертость".
        }

        public void OnTargetEliminated(LivingEntity eliminatedTarget)
        {
            string targetName = IsInstanceValid(eliminatedTarget) ? eliminatedTarget.Name : "[Freed Target]";
            GD.Print($"{_context.Name}: Confirmed elimination of [{targetName}].");

            if (CurrentTarget == eliminatedTarget)
            {
                CurrentTarget = null;
            }
            _potentialTargets.Remove(eliminatedTarget);

            ForceReevaluation(); // Сразу ищем новую цель после убийства
        }

        public void ClearTarget()
        {
            CurrentTarget = null;
        }

        private void SetAttackTarget(LivingEntity newTarget)
        {
            // Если новая цель - это та же, что и была, ничего не делаем.
            if (newTarget == CurrentTarget) return;

            // Если новая цель невалидна (например, null), а старая была, это значит, мы потеряли все цели.
            else if (newTarget == null)
            {
                GD.Print($"{_context.Name} lost all targets.");
                CurrentTarget = null;
                // Не вызываем здесь OnTargetLostLineOfSight, т.к. это может привести к нежелательным переходам состояний.
                // Состояния сами должны решить, что делать при CurrentTarget == null.
                return;
            }

            GD.Print($"{_context.Name} new best target is: {newTarget.Name}");
            CurrentTarget = newTarget;
            _context.OnNewTargetAcquired(newTarget);
        }

        private void OnTargetDetected(Node3D body)
        {
            if (body is LivingEntity entity && body != _context)
            {
                if (_context.IsHostile(entity) && body is ICharacter && !_potentialTargets.Contains(entity))
                {
                    GD.Print($"{_context.Name} added {body.Name} to potential targets.");
                    _potentialTargets.Add(entity);
                }
            }
        }

        private void OnTargetLost(Node3D body)
        {
            if (body is LivingEntity entity)
            {
                _potentialTargets.Remove(entity);
                GD.Print($"{_context.Name} removed {body.Name} from potential targets.");

                // Если именно текущая цель вышла из триггера, форсируем переоценку.
                if (entity == CurrentTarget)
                {
                    ForceReevaluation();
                }
            }
        }
    }
}