using Godot;
using Game.Entity.AI.Profiles;

namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Компонент, отвечающий за физическое перемещение и вращение AIEntity.
    /// Получает команды от AIEntity и его состояний, а сам реализует их в _PhysicsProcess.
    /// </summary>
    public partial class AIMovementController : Node
    {
        [ExportGroup("Dependencies")]
        [Export] private NavigationAgent3D _navigationAgent;
        [Export] private Node3D _headPivot;

        private AIEntity _context;
        private AIMovementProfile _movementProfile;
        private AILookProfile _lookProfile;

        public NavigationAgent3D NavigationAgent => _navigationAgent;
        public Vector3 TargetVelocity { get; set; }

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
            if (_navigationAgent == null)
            {
                GD.PushError($"NavigationAgent3D not assigned to {Name}!");
                SetPhysicsProcess(false);
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            // 1. Управление движением
            if (!_navigationAgent.IsNavigationFinished())
            {
                var nextPoint = _navigationAgent.GetNextPathPosition();
                var direction = _context.GlobalPosition.DirectionTo(nextPoint);
                TargetVelocity = direction * _context.Speed;
            }
            else
            {
                TargetVelocity = Vector3.Zero;
            }
            _context.Velocity = _context.Velocity.Lerp(TargetVelocity, _context.Acceleration * (float)delta);

            // 2. Вращение тела
            RotateBody((float)delta);

            // 3. Вращение головы
            RotateHead((float)delta);

            _context.MoveAndSlide();
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
            if (_navigationAgent.TargetPosition == targetPosition) return;
            _navigationAgent.TargetPosition = targetPosition;
        }

        public void StopMovement()
        {
            _navigationAgent.TargetPosition = _context.GlobalPosition;
        }
    }
}