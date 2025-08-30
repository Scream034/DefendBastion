using Godot;
using Game.Interfaces;
using System.Collections.Generic;

namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Компонент, отвечающий за обнаружение, отслеживание и выбор целей для ИИ.
    /// Инкапсулирует работу с Area3D и логику оценки угроз.
    /// </summary>
    public partial class AITargetingSystem : Node
    {
        [ExportGroup("Dependencies")]
        [Export] private Area3D _targetDetectionArea;

        private AIEntity _context;
        private float _targetEvaluationTimer;
        private readonly List<LivingEntity> _potentialTargets = [];

        public LivingEntity CurrentTarget { get; private set; }
        public IReadOnlyList<PhysicsBody3D> PotentialTargets => _potentialTargets;

        public void Initialize(AIEntity context)
        {
            _context = context;
            if (_context.Profile?.CombatProfile == null)
            {
                GD.PushError($"AICombatProfile is not set for {_context.Name}. Targeting system will not work.");
                SetProcess(false);
                return;
            }

            _targetEvaluationTimer = _context.Profile.CombatProfile.TargetEvaluationInterval / ProjectSettings.GetSetting("physics/common/physics_ticks_per_second").AsInt32();

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

        private void EvaluateTargets()
        {
            _potentialTargets.RemoveAll(target => !IsInstanceValid(target) || (target is ICharacter character && character.Health <= 0));

            if (_potentialTargets.Count == 0 && CurrentTarget == null)
            {
                return;
            }

            var bestTarget = AITargetEvaluator.GetBestTarget(_context, _potentialTargets);

            if (bestTarget != null)
            {
                SetAttackTarget(bestTarget);
            }
            else if (CurrentTarget != null && (_potentialTargets.Count == 0 || !_potentialTargets.Contains(CurrentTarget)))
            {
                // Если текущая цель больше не в списке потенциальных (но еще жива), возможно, она просто вышла из триггера
                // В этом случае логика состояний (Pursuit) должна сама решить, что делать.
                // Здесь мы просто перестаем ее считать "лучшей", но не сбрасываем, чтобы состояние могло отреагировать.
            }
        }

        public void OnTargetEliminated(LivingEntity eliminatedTarget)
        {
            // Это предотвращает ObjectDisposedException, если цель была удалена в том же кадре.
            string targetName = IsInstanceValid(eliminatedTarget) ? eliminatedTarget.Name : "[Freed Target]";
            GD.Print($"{_context.Name}: Confirmed elimination of [{targetName}].");

            // Сравнение с `null` или уничтоженным объектом безопасно.
            if (CurrentTarget == eliminatedTarget)
            {
                CurrentTarget = null;
            }

            // `Remove` также безопасно обработает null или уничтоженную ссылку.
            _potentialTargets.Remove(eliminatedTarget);

            EvaluateTargets();
        }

        public void ClearTarget()
        {
            CurrentTarget = null;
        }

        private void SetAttackTarget(LivingEntity target)
        {
            if (target == _context || target == CurrentTarget) return;

            if (target is not ICharacter || target is not IFactionMember)
            {
                GD.PrintErr($"Attempted to target {target.Name}, which is not a valid target.");
                return;
            }

            CurrentTarget = target;
            GD.Print($"{_context.Name} new best target is: {target.Name}");

            _context.OnNewTargetAcquired(target);
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
                _context.OnTargetLostLineOfSight(entity);
            }
        }
    }
}