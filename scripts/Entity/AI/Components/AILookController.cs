using Godot;
using Game.Entity.AI.Profiles;

namespace Game.Entity.AI.Components
{
    public partial class AILookController : Node
    {
        private AIEntity _context;
        private AILookProfile _profile;

        private Vector3? _interestPoint;
        private Vector3? _priorityLookTarget; // Новое: Приоритетная точка (угроза)
        private float _priorityLookTimer;

        private float _glanceTimer;
        private bool _isGlancingAtInterestPoint;

        public Vector3 CurrentLookTarget { get; private set; }

        // Событие, когда шея достигает предела
        [Signal]
        public delegate void LookLimitReachedEventHandler(Vector3 targetDirection);

        public void Initialize(AIEntity context)
        {
            _context = context;
            _profile = _context.Profile?.LookProfile;
            if (_profile == null)
            {
                GD.PushError($"AILookProfile is not set for {_context.Name}. Look controller will not work.");
                SetProcess(false);
            }
        }

        public override void _Process(double delta)
        {
            Vector3 finalLookPosition;

            // 1. Приоритет: Угроза (получение урона/снаряд)
            if (_priorityLookTimer > 0 && _priorityLookTarget.HasValue)
            {
                _priorityLookTimer -= (float)delta;
                finalLookPosition = _priorityLookTarget.Value;

                // Проверяем, нужно ли повернуть тело
                CheckHeadLimitsAndRequestBodyTurn(finalLookPosition);
            }
            // 2. Приоритет: Текущая цель атаки
            else if (GodotObject.IsInstanceValid(_context.TargetingSystem.CurrentTarget))
            {
                finalLookPosition = _context.TargetingSystem.CurrentTarget.GlobalPosition;
            }
            // 3. Приоритет: Точка интереса (движение, патруль)
            else
            {
                finalLookPosition = CalculateStandardLook(delta);
            }

            CurrentLookTarget = finalLookPosition;
        }

        /// <summary>
        /// Принудительно заставляет ИИ смотреть в точку на время.
        /// </summary>
        public void SetPriorityLookTarget(Vector3 target, float duration)
        {
            _priorityLookTarget = target;
            _priorityLookTimer = duration;
        }

        private void CheckNeckLimits(Vector3 targetPos)
        {
            // Если лимиты отключены, ничего не делаем
            if (_context.Profile?.LookProfile?.EnableHeadRotationLimits != true) return;

            // Вектор "вперед" относительно тела
            Vector3 bodyForward = _context.GlobalBasis.Z;
            Vector3 dirToTarget = _context.GlobalPosition.DirectionTo(targetPos);

            // Считаем угол
            float angle = bodyForward.AngleTo(dirToTarget);
            float limitRad = Mathf.DegToRad(_context.Profile.LookProfile.MaxHeadYawDegrees);

            // Если угол к угрозе больше, чем может повернуть голова...
            if (angle > limitRad)
            {
                // ...командуем телу развернуться!
                _context.MovementController.RequestImmediateFaceDirection(dirToTarget);
            }
        }

        private Vector3 CalculateStandardLook(double delta)
        {
            Vector3 lookDirection = _context.Velocity.IsZeroApprox() ? _context.Basis.Z : _context.Velocity.Normalized();
            Vector3 pos = _context.GlobalPosition + lookDirection * 10f;

            if (_profile.LookAtInterestPointWhileMoving && _interestPoint.HasValue && _context.IsMoving)
            {
                _glanceTimer -= (float)delta;
                if (_glanceTimer <= 0f)
                {
                    _isGlancingAtInterestPoint = !_isGlancingAtInterestPoint;
                    _glanceTimer = _isGlancingAtInterestPoint
                        ? (float)GD.RandRange(_profile.MinGlanceDuration, _profile.MaxGlanceDuration)
                        : (float)GD.RandRange(_profile.MinLookForwardDuration, _profile.MaxLookForwardDuration);
                }

                if (_isGlancingAtInterestPoint)
                {
                    pos = _interestPoint.Value;
                }
            }
            return pos;
        }

        private void CheckHeadLimitsAndRequestBodyTurn(Vector3 targetPos)
        {
            if (!_profile.EnableHeadRotationLimits) return;

            Vector3 directionToTarget = _context.GlobalPosition.DirectionTo(targetPos);
            Vector3 forward = _context.GlobalBasis.Z; // Предполагаем Forward = Z

            // Считаем угол между "куда смотрит тело" и "где угроза"
            float angle = forward.AngleTo(directionToTarget); // Радианы
            float maxYaw = Mathf.DegToRad(_profile.MaxHeadYawDegrees);

            // Если угол больше, чем позволяет шея -> запрашиваем поворот тела
            if (angle > maxYaw)
            {
                _context.MovementController.RequestImmediateFaceDirection(directionToTarget);
            }
        }

        public void SetInterestPoint(Vector3? targetPosition)
        {
            _interestPoint = targetPosition;
            // Сбрасываем таймер взгляда, чтобы сразу посмотреть (если не в приоритетном режиме)
            if (_priorityLookTimer <= 0)
            {
                _glanceTimer = 0;
                _isGlancingAtInterestPoint = true;
            }
        }
    }
}