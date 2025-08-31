using Godot;
using Game.Entity.AI.Profiles;
using System.Linq;

namespace Game.Entity.AI.Components
{
    public partial class AIMovementController : Node
    {
        [ExportGroup("Dependencies")]
        [Export] private NavigationAgent3D _navigationAgent;
        [Export] private Node3D _headPivot;

        // --- Настройки Separation Force ---
        [Export] private float _separationRadius = 3.0f; // Радиус, в котором ищем соседей для избегания
        [Export] private float _separationWeight = 5.0f; // Сила отталкивания

        private AIEntity _context;
        private AIMovementProfile _movementProfile;
        private AILookProfile _lookProfile;

        public NavigationAgent3D NavigationAgent => _navigationAgent;
        public Vector3 TargetVelocity { get; set; }

        public void Initialize(AIEntity context)
        {
            // ... (Существующая логика инициализации) ...
            _context = context;
            _movementProfile = context.Profile?.MovementProfile;
            _lookProfile = context.Profile?.LookProfile;

            if (_movementProfile == null || _lookProfile == null)
            {
                GD.PushError($"AI Profile (Movement/Look) is not set for {_context.Name}. Movement controller disabled.");
                SetPhysicsProcess(false);
                return;
            }
            if (_navigationAgent == null)
            {
                GD.PushError($"NavigationAgent3D not assigned to {Name}!");
                SetPhysicsProcess(false);
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            // 1. Расчет базовой навигации (сила притяжения к цели)
            Vector3 navigationVelocity = Vector3.Zero;
            if (!_navigationAgent.IsNavigationFinished())
            {
                var nextPoint = _navigationAgent.GetNextPathPosition();
                navigationVelocity = _context.GlobalPosition.DirectionTo(nextPoint) * _context.Speed;
            }

            // 2. Расчет Separation Force (сила отталкивания от союзников)
            Vector3 separationForce = CalculateSeparationForce();

            // 3. Комбинирование сил.
            // Мы даем приоритет Separation, чтобы избежать столкновения, 
            // но навигация остается основной движущей силой.

            // Навигационная скорость преобразуется в Steering Force:
            Vector3 steeringForce = navigationVelocity - _context.Velocity;

            // Итоговая сила: Навигационное намерение + Отталкивание
            Vector3 finalForce = steeringForce + separationForce;

            // Ограничиваем силу ускорением
            TargetVelocity = (_context.Velocity + finalForce).LimitLength(_context.Speed);

            _context.Velocity = _context.Velocity.Lerp(TargetVelocity, _context.Acceleration * (float)delta);

            // 4. Управление вращением и MoveAndSlide
            RotateBody((float)delta);
            RotateHead((float)delta);

            _context.MoveAndSlide();
        }

        /// <summary>
        /// Вычисляет силу, необходимую для отталкивания от ближайших союзников.
        /// Реализует паттерн Separation из Flocking.
        /// </summary>
        private Vector3 CalculateSeparationForce()
        {
            if (_context.Squad == null || _context.Squad.Members.Count <= 1) return Vector3.Zero;

            Vector3 steering = Vector3.Zero;
            int neighborCount = 0;

            // Используем только тех, кто находится в радиусе отделения
            var closeAllies = _context.Squad.Members
                .Where(m => m != _context && m.GlobalPosition.DistanceSquaredTo(_context.GlobalPosition) < _separationRadius * _separationRadius);

            foreach (var ally in closeAllies)
            {
                if (!IsInstanceValid(ally)) continue;

                Vector3 direction = _context.GlobalPosition - ally.GlobalPosition;
                float distance = direction.Length();

                if (distance > 0)
                {
                    // Сила тем больше, чем ближе юнит. (1/distance)
                    // Нормализуем направление и масштабируем по инверсии квадрата расстояния.
                    float strength = 1.0f / distance;
                    steering += direction.Normalized() * strength;
                    neighborCount++;
                }
            }

            if (neighborCount > 0)
            {
                steering /= neighborCount; // Усредняем силу
                steering = steering.Normalized() * _context.Speed; // Масштабируем до максимальной скорости
                steering = steering - _context.Velocity; // Преобразуем в Steering Force
                steering = steering.LimitLength(_separationWeight); // Применяем вес
            }

            return steering;
        }

        private void RotateBody(float delta)
        {
            // Приоритет №1: Если мы в бою и стоим на месте, поворачиваем тело к врагу.
            if (_context.IsInCombat && IsInstanceValid(_context.TargetingSystem.CurrentTarget) && TargetVelocity.IsZeroApprox())
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
            // Здесь дополнительно проверяем, не конфликтует ли позиция с кем-то в AITacticalCoordinator.
            // В нашей текущей архитектуре эту проверку делает Squad перед выдачей приказа,
            // но на всякий случай можно добавить логику пересчета пути, если позиция занята.
            // Однако, в целях KISS и производительности, мы полагаемся на Separation Force
            // и на то, что Squad выдал валидные, непересекающиеся точки.
            if (_navigationAgent.TargetPosition == targetPosition) return;
            _navigationAgent.TargetPosition = targetPosition;
            AITacticalCoordinator.ReservePosition(_context, targetPosition);
        }

        public void StopMovement()
        {
            _navigationAgent.TargetPosition = _context.GlobalPosition;
            AITacticalCoordinator.ReleasePosition(_context);
        }
    }
}