using Godot;
using Game.Entity.AI.Profiles;

namespace Game.Entity.AI.Components
{
    /// <summary>
    /// Контроллер процедурного взгляда.
    /// Управляет приоритетами целей и применяет сглаживание/шум для естественности.
    /// </summary>
    public partial class AILookController : Node
    {
        private enum LookSource
        {
            None,
            Forward,
            Casual,
            Investigation,
            DamageReaction,
            Combat,
            ManualOverride
        }

        private AIEntity _context;
        private AILookProfile _profile;
        private FastNoiseLite _noise; // Генератор шума для "живого" дрожания
        private float _noiseTime;

        // Источники данных
        private Vector3? _forcedLookTarget;
        private Vector3? _threatSourcePosition;
        private float _threatTimer;
        private Vector3? _interestPoint;
        private float _scanTimer;
        private Vector3 _currentScanOffset;

        /// <summary>
        /// "Сырая" целевая точка, выбранная системой приоритетов.
        /// </summary>
        public Vector3 RawTargetPosition { get; private set; }

        /// <summary>
        /// Финальная сглаженная точка, на которую должна смотреть модель (IK Target).
        /// Используйте это свойство для привязки AnimationTree или костей.
        /// </summary>
        public Vector3 FinalLookPosition { get; private set; }

        private LookSource _currentSource = LookSource.None;

        /// <summary>
        /// Инициализирует контроллер и генератор шума.
        /// </summary>
        public void Initialize(AIEntity context)
        {
            _context = context;
            _profile = _context.Profile?.LookProfile;

            if (_profile == null)
            {
                GD.PushError($"AILookProfile is not set for {_context.Name}.");
                SetProcess(false);
                return;
            }

            // Настройка шума для органики
            _noise = new FastNoiseLite
            {
                NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
                Frequency = 1.0f // Базовая частота, будем масштабировать временем
            };

            // Стартовая позиция
            FinalLookPosition = _context.GlobalPosition + _context.GlobalBasis.Z * 5f;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_profile == null) return;

            float dt = (float)delta;
            UpdateTimers(dt);

            // 1. Выбор цели (Логика приоритетов)
            EvaluatePriorities(dt);

            // 2. Добавление шума (Микро-движения)
            Vector3 noisyTarget = ApplyNoiseToTarget(RawTargetPosition, dt);

            // 3. Сглаживание (Интерполяция)
            FinalLookPosition = FinalLookPosition.Lerp(noisyTarget, dt * _profile.LookSmoothingSpeed);
        }

        private void UpdateTimers(float delta)
        {
            if (_threatTimer > 0) _threatTimer -= delta;
        }

        private Vector3 ApplyNoiseToTarget(Vector3 target, float delta)
        {
            // Если амплитуда 0, не тратим ресурсы
            if (_profile.NoiseAmplitude <= 0.001f) return target;

            _noiseTime += delta * _profile.NoiseFrequency;

            // Генерация 3D смещения на основе времени
            float x = _noise.GetNoise2D(_noiseTime, 0f);
            float y = _noise.GetNoise2D(_noiseTime, 100f); // Сдвиг для уникальности оси
            float z = _noise.GetNoise2D(_noiseTime, 200f);

            Vector3 noiseOffset = new Vector3(x, y, z) * _profile.NoiseAmplitude;
            return target + noiseOffset;
        }

        private void EvaluatePriorities(float delta)
        {
            // 1. MANUAL OVERRIDE
            if (_forcedLookTarget.HasValue)
            {
                SetRawTarget(_forcedLookTarget.Value, LookSource.ManualOverride);
                return;
            }

            // 2. COMBAT
            var combatTarget = _context.TargetingSystem?.CurrentTarget;
            if (IsInstanceValid(combatTarget))
            {
                int currentWeight = _profile.PriorityCombat;

                // Проверка: перевешивает ли реакция на урон текущий бой?
                if (_threatTimer > 0 && _threatSourcePosition.HasValue && _profile.PriorityDamageReaction > currentWeight)
                {
                    SetRawTarget(_threatSourcePosition.Value, LookSource.DamageReaction);
                    return;
                }

                // В бою обычно смотрим чуть выше центра (в голову/грудь), это можно уточнять в TargetingSystem
                SetRawTarget(combatTarget.GlobalPosition, LookSource.Combat);
                return;
            }

            // 3. DAMAGE REACTION
            if (_threatTimer > 0 && _threatSourcePosition.HasValue)
            {
                SetRawTarget(_threatSourcePosition.Value, LookSource.DamageReaction);
                return;
            }

            // 4. INVESTIGATION
            if (_interestPoint.HasValue)
            {
                SetRawTarget(_interestPoint.Value, LookSource.Investigation);
                return;
            }

            if (HandleScanningBehavior(delta, out Vector3 scanTarget))
            {
                SetRawTarget(scanTarget, LookSource.Investigation);
                return;
            }

            // 5. CASUAL / FORWARD
            Vector3 forwardLook = GetForwardLook();
            SetRawTarget(forwardLook, _context.IsMoving ? LookSource.Forward : LookSource.Casual);
        }

        private void SetRawTarget(Vector3 target, LookSource source)
        {
            RawTargetPosition = target;
            _currentSource = source;
        }

        public void SetForcedTarget(Vector3? target) => _forcedLookTarget = target;
        public void SetInterestPoint(Vector3? point) => _interestPoint = point;

        public void ReportThreat(Vector3 sourcePosition, float duration = 2.0f)
        {
            // Если бой важнее реакции, игнорируем (например, терминатор не дергается)
            if (_currentSource == LookSource.Combat && _profile.PriorityCombat >= _profile.PriorityDamageReaction)
                return;

            _threatSourcePosition = sourcePosition;
            _threatTimer = duration;
        }

        private bool HandleScanningBehavior(float delta, out Vector3 target)
        {
            target = Vector3.Zero;
            if (_context.IsMoving) return false;

            _scanTimer -= delta;
            if (_scanTimer <= 0)
            {
                _scanTimer = (float)GD.RandRange(2.0, 5.0);
                float angle = (float)GD.RandRange(-Mathf.Pi / 4, Mathf.Pi / 4); // +/- 45 градусов
                Vector3 forward = _context.GlobalBasis.Z;
                _currentScanOffset = forward.Rotated(Vector3.Up, angle) * 8f;
            }

            target = _context.GlobalPosition + _currentScanOffset;
            return true;
        }

        private Vector3 GetForwardLook()
        {
            Vector3 dir = _context.Velocity.LengthSquared() > 0.1f
                ? _context.Velocity.Normalized()
                : _context.GlobalBasis.Z;
            return _context.GlobalPosition + dir * 10f;
        }
    }
}