using Godot;
using Game.Entity.AI.Profiles;

namespace Game.Entity.AI.Components
{
    public partial class AIMovementController : Node
    {
        [ExportGroup("Dependencies")]
        [Export] public NavigationAgent3D NavigationAgent;
        [Export] private Node3D _headPivot;

        private AIEntity _context;
        private AIMovementProfile _movementProfile;
        private AILookProfile _lookProfile;

        // <--- ИЗМЕНЕНИЕ: Добавляем свойства, которые ты хотел использовать ---
        /// <summary>
        /// Дистанция до цели, на которой агент считает путь завершенным (в квадрате для оптимизации).
        /// </summary>
        public float TargetDesiredDistanceSq { get; private set; }

        /// <summary>
        /// Линейный радиус для расчетов безопасного расстояния.
        /// </summary>
        public float SeparationRadius { get; private set; }

        public Vector3 TargetVelocity { get; set; }

        public Vector3? GetTargetPosition()
        {
            // NavigationAgent.TargetPosition может быть не тем, что нам нужно,
            // если агент уже близко к цели. Лучше хранить цель самим.
            // Давайте улучшим.
            if (NavigationAgent.IsTargetReachable())
            {
                return NavigationAgent.TargetPosition;
            }
            return null;
        }

        public bool IsMoving() => !NavigationAgent.IsNavigationFinished();

        public bool HasReachedDestination()
        {
            // Этот метод используется для проверки, завершил ли AI свой путь.
            return NavigationAgent.IsNavigationFinished();
        }

        public void Initialize(AIEntity context)
        {
            _context = context;
            _movementProfile = context.Profile?.MovementProfile;
            _lookProfile = context.Profile?.LookProfile;

            if (_movementProfile == null || _lookProfile == null)
            {
                GD.PushError($"AI Profile (Movement/Look) is not set for {_context.Name}. Movement controller disabled.");
                SetPhysicsProcess(false);
                return;
            }
            if (NavigationAgent == null)
            {
                GD.PushError($"NavigationAgent3D not assigned to {Name}!");
                SetPhysicsProcess(false);
                return;
            }

            // <--- ИЗМЕНЕНИЕ: Инициализируем новые свойства ---
            TargetDesiredDistanceSq = NavigationAgent.TargetDesiredDistance * NavigationAgent.TargetDesiredDistance;
            // Используем радиус из NavigationAgent как основу для всех расчетов дистанции
            SeparationRadius = NavigationAgent.Radius;
        }

        public override void _PhysicsProcess(double delta)
        {
            // 1. Расчет базовой навигации (сила притяжения к цели)
            Vector3 navigationVelocity = Vector3.Zero;
            if (!NavigationAgent.IsNavigationFinished())
            {
                var nextPoint = NavigationAgent.GetNextPathPosition();
                navigationVelocity = _context.GlobalPosition.DirectionTo(nextPoint) * _context.Speed;
            }

            // <--- ИЗМЕНЕНИЕ: Полностью убираем кастомную логику Separation ---
            // Вся логика избегания агентов теперь лежит на NavigationAgent3D (avoidance_enabled = true)
            // Vector3 separationForce = CalculateSeparationForce();

            // Навигационная скорость преобразуется в Steering Force:
            Vector3 steeringForce = navigationVelocity - _context.Velocity;

            // Итоговая сила: теперь это только навигационное намерение.
            Vector3 finalForce = steeringForce;

            TargetVelocity = (_context.Velocity + finalForce).LimitLength(_context.Speed);
            _context.Velocity = _context.Velocity.Lerp(TargetVelocity, _context.Acceleration * (float)delta);

            RotateBody((float)delta);
            RotateHead((float)delta);

            _context.MoveAndSlide();
        }

        private void RotateBody(float delta)
        {
            // Приоритет №1: Если мы в бою и стоим на месте, поворачиваем тело к врагу.
            if (_context.IsInCombat && IsInstanceValid(_context.TargetingSystem.CurrentTarget) && NavigationAgent.Velocity.IsZeroApprox())
            {
                var directionToTarget = _context.GlobalPosition.DirectionTo(_context.TargetingSystem.CurrentTarget.GlobalPosition) with { Y = 0 };
                if (!directionToTarget.IsZeroApprox())
                {
                    var targetRotation = Basis.LookingAt(directionToTarget.Normalized()).Orthonormalized();
                    _context.Basis = _context.Basis.Orthonormalized().Slerp(targetRotation, _movementProfile.BodyRotationSpeed * delta);
                }
            }
            // Стандартное поведение: Поворачиваем тело по направлению движения.
            else
            {
                var horizontalVelocity = _context.Velocity with { Y = 0 };
                if (horizontalVelocity.LengthSquared() > 0.01f)
                {
                    var targetRotation = Basis.LookingAt(horizontalVelocity.Normalized()).Orthonormalized();
                    _context.Basis = _context.Basis.Orthonormalized().Slerp(targetRotation, _movementProfile.BodyRotationSpeed * delta);
                }
            }
        }

        private void RotateHead(float delta)
        {
            if (_headPivot == null) return;

            // Получаем цель для взгляда от LookController
            Vector3 lookTargetPosition = _context.LookController.CurrentLookTarget;

            if (lookTargetPosition.IsEqualApprox(_context.GlobalPosition)) return;

            var localTarget = _headPivot.ToLocal(lookTargetPosition).Normalized();

            const float verticalThreshold = 0.999f;
            if (Mathf.Abs(localTarget.Dot(Vector3.Up)) > verticalThreshold) return;

            var targetRotation = Basis.LookingAt(localTarget).Orthonormalized();

            if (_lookProfile.EnableHeadRotationLimits)
            {
                var euler = targetRotation.GetEuler();
                float yawLimit = Mathf.DegToRad(_lookProfile.MaxHeadYawDegrees);
                float pitchLimit = Mathf.DegToRad(_lookProfile.MaxHeadPitchDegrees);
                euler.Y = Mathf.Clamp(euler.Y, -yawLimit, yawLimit);
                euler.X = Mathf.Clamp(euler.X, -pitchLimit, pitchLimit);
                targetRotation = Basis.FromEuler(euler);
            }

            _headPivot.Basis = _headPivot.Basis.Orthonormalized().Slerp(targetRotation, _movementProfile.HeadRotationSpeed * delta);
        }

        public void MoveTo(Vector3 targetPosition)
        {
            if (NavigationAgent.TargetPosition.IsEqualApprox(targetPosition)) return;
            NavigationAgent.TargetPosition = targetPosition;
            AITacticalCoordinator.ReservePosition(_context, targetPosition);
            GD.Print($"{_context.Name} MoveTo {targetPosition}");
        }

        public void StopMovement()
        {
            // Делаем остановку более явной и быстрой 
            // Мы не только меняем цель навигатора, но и напрямую обнуляем целевую скорость.
            // Это заставит юнит остановиться и начать поворот к врагу практически мгновенно.
            NavigationAgent.TargetPosition = _context.GlobalPosition;
            TargetVelocity = Vector3.Zero;
            AITacticalCoordinator.ReleasePosition(_context);
        }
    }
}