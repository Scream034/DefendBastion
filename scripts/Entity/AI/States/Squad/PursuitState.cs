using Godot;
using Game.Entity.AI.Components;

namespace Game.Entity.AI.States.Squad
{
    public class PursuitState : SquadStateBase
    {
        // РЕШЕНИЕ 2: Замена Timer'ов
        private double _reevaluateTimer;
        private double _targetVelocityTimer;

        private Vector3 _lastKnownTargetPosition;
        private Vector3 _observedTargetVelocity;
        private Vector3 _targetPreviousPosition;

        // Порог для выхода из преследования
        private readonly float _exitPursuitSpeedSq;

        public PursuitState(AISquad squad, LivingEntity target) : base(squad)
        {
            Squad.CurrentTarget = target;
            float exitThreshold = Squad.TargetMovementThreshold * Squad.PursuitExitThresholdFactor;
            _exitPursuitSpeedSq = exitThreshold * exitThreshold;
        }

        public override void Enter()
        {
            GD.Print($"Squad '{Squad.SquadName}' is now in PURSUIT mode against {Squad.CurrentTarget.Name}.");
            _lastKnownTargetPosition = Squad.CurrentTarget.GlobalPosition;
            _targetPreviousPosition = Squad.CurrentTarget.GlobalPosition;

            // Инициализируем счетчики
            _reevaluateTimer = 1.0f; // В преследовании обновляемся чаще
            _targetVelocityTimer = Squad.TargetVelocityTrackInterval;

            // Немедленно вычисляем первую точку
            ExecutePursuitTactic();
        }

        public override void Process(double delta)
        {
            if (!GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;

            _reevaluateTimer -= delta;
            _targetVelocityTimer -= delta;

            if (_targetVelocityTimer <= 0)
            {
                UpdateTargetVelocity();
                _targetVelocityTimer = Squad.TargetVelocityTrackInterval;
            }

            if (_reevaluateTimer <= 0)
            {
                ExecutePursuitTactic();
                _reevaluateTimer = 1.0f;
            }

            // РЕШЕНИЕ 4: Используем кэшированное значение
            if (_observedTargetVelocity.LengthSquared() < _exitPursuitSpeedSq)
            {
                GD.Print($"Target has slowed down. Switching to standard COMBAT mode.");
                Squad.ChangeState(new CombatState(Squad, Squad.CurrentTarget));
                return;
            }

            // Обновляем позицию цели, если видим ее (цикл без LINQ - это уже хорошо)
            foreach (var member in Squad.Members)
            {
                if (member.GetVisibleTargetPoint(Squad.CurrentTarget).HasValue)
                {
                    _lastKnownTargetPosition = Squad.CurrentTarget.GlobalPosition;
                    break;
                }
            }
        }

        private void UpdateTargetVelocity()
        {
            if (!GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;
            var displacement = Squad.CurrentTarget.GlobalPosition - _targetPreviousPosition;
            _observedTargetVelocity = displacement / Squad.TargetVelocityTrackInterval;
            _targetPreviousPosition = Squad.CurrentTarget.GlobalPosition;
        }

        private void ExecutePursuitTactic()
        {
            if (!GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;

            // Рассчитываем упреждающую точку
            Vector3 pursuitPoint = _lastKnownTargetPosition + (_observedTargetVelocity * Squad.PursuitPredictionTime);
            GD.Print($"Pursuit prediction: To {pursuitPoint} using observed velocity {_observedTargetVelocity.Length():F1} m/s");

            foreach (var member in Squad.Members)
            {
                // Каждый боец пытается занять позицию на своей оптимальной дистанции от упреждающей точки
                float engagementDistance = member.CombatBehavior.AttackRange * member.Profile.CombatProfile.EngagementRangeFactor;
                Vector3 directionFromPursuitPoint = (member.GlobalPosition - pursuitPoint).Normalized();

                // Если боец оказался прямо в точке, даем случайное направление
                if (directionFromPursuitPoint.IsZeroApprox())
                    directionFromPursuitPoint = Vector3.Forward.Rotated(Vector3.Up, (float)GD.RandRange(0, Mathf.Pi * 2));

                var targetPos = pursuitPoint + directionFromPursuitPoint * engagementDistance;

                member.ReceiveOrderMoveTo(targetPos);
                member.ReceiveOrderAttackTarget(Squad.CurrentTarget);
            }
        }
    }
}