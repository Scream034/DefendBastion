using Godot;
using System.Collections.Generic;
using System.Linq;
using Game.Entity.AI.Orchestrator;

namespace Game.Entity.AI.Components
{
    public enum SquadState { Idle, MovingToPoint, FollowingPath, InCombat, Pursuing }
    public enum SquadTask { Standby, PatrolPath, AssaultPath }

    public partial class AISquad : Node
    {
        [ExportGroup("Configuration")]
        [Export] public string SquadName;
        [Export] public SquadTask Task = SquadTask.Standby;
        [Export] public Formation MarchingFormation;
        [Export] public Formation CombatFormation;
        [Export] public Path3D MissionPath;

        [ExportGroup("Task Settings")]
        [Export] private float _pathWaypointThreshold = 2f;

        /// <summary>
        /// Интервал для проверки смены позиций (в секундах)
        /// </summary>
        [ExportGroup("Dynamic Behavior")]
        [Export] private float _repositionCheckInterval = 4f;
        /// <summary>
        /// Время перезарядки для проверки смены позиций (в секундах)
        /// </summary>
        [Export] private float _repositionCooldown = 1.5f;
        /// <summary>
        /// Время на которое мы пытаемся предугадать движение нашей цели при преследовании
        /// </summary>
        [Export] private float _pursuitPredictionTime = 2.2f;
        /// <summary>
        /// Как часто мы замеряем "темп" цели (в секундах).
        /// </summary>
        [Export] private float _targetVelocityTrackInterval = 0.4f;
        /// <summary>
        /// Для определения "подвижной" цели 
        /// </summary>
        [Export] private float _targetMovementThreshold = 6f;
        private double _repositionTimer;
        private double _currentRepositionCooldown;

        // Попытка преследования
        private Vector3 _lastKnownTargetPosition;
        private int _failedRepositionAttempts = 0; // Счетчик неудачных попыток найти позицию

        // Преследование
        private Vector3 _observedTargetVelocity; // Наш рассчитанный "темп"
        private Vector3 _targetPreviousPosition; // Где была цель при прошлой проверке
        private double _timeSinceLastVelCheck;   // Таймер для интервала проверок

        // Поворот формации
        [Export] private float _orientationUpdateInterval = 0.3f; // Как часто обновлять "взгляд" формации (в секундах)
        private double _orientationUpdateTimer;

        // Хранилище для текущего тактического плана
        private bool _isUsingFormationTactic = false; // Флаг, что мы сейчас используем тактику, которую можно вращать
        private Vector3 _squadAnchorPoint; // "Якорь", центр нашей формации
        private Dictionary<AIEntity, Vector3> _formationLocalOffsets = new(); // Локальные смещения бойцов относительно якоря

        // Движение по пути
        private readonly HashSet<AIEntity> _membersAtDestination = [];
        private Vector3 _squadCenterCache;

        private Vector3[] _pathPoints;
        private int _currentPathIndex = 0;
        private int _pathDirection = 1;

        /// <summary>
        /// Сет для отслеживания бойцов, которые считают свою позицию плохой
        /// </summary>
        private readonly HashSet<AIEntity> _membersRequestingReposition = [];

        public readonly List<AIEntity> Members = [];
        public SquadState CurrentState { get; private set; } = SquadState.Idle;
        public LivingEntity CurrentTarget { get; private set; }

        public override void _Ready()
        {
            if (string.IsNullOrEmpty(SquadName))
            {
                GD.PushError($"AISquad '{Name}' has no SquadName assigned!");
                SetProcess(false);
                return;
            }
            Orchestrator.LegionBrain.Instance.RegisterSquad(this);
            SetProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (Members.Count == 0) return;

            // --- Логика следования по пути ---
            if (CurrentState == SquadState.FollowingPath)
            {
                UpdateSquadCenter();
                var targetWaypoint = MissionPath.ToGlobal(_pathPoints[_currentPathIndex]);
                if (_squadCenterCache.DistanceSquaredTo(targetWaypoint) < _pathWaypointThreshold * _pathWaypointThreshold)
                {
                    MoveToNextWaypoint();
                }
            }

            // Далее код использует текущую цель
            if (!IsInstanceValid(CurrentTarget)) return;

            // --- Логика вращения формации ---
            if (CurrentState == SquadState.InCombat)
            {
                _orientationUpdateTimer -= delta;
                if (_orientationUpdateTimer <= 0f && _isUsingFormationTactic)
                {
                    UpdateFormationOrientation();
                    _orientationUpdateTimer = _orientationUpdateInterval;
                }
            }

            // --- Обновленная боевая логика ---
            if (CurrentState == SquadState.InCombat || CurrentState == SquadState.Pursuing)
            {
                _timeSinceLastVelCheck += delta;

                // Обновляем "темп" цели по таймеру
                if (_timeSinceLastVelCheck >= _targetVelocityTrackInterval)
                {
                    var displacement = CurrentTarget.GlobalPosition - _targetPreviousPosition;
                    _observedTargetVelocity = displacement / (float)_timeSinceLastVelCheck;
                    _targetPreviousPosition = CurrentTarget.GlobalPosition;
                    _timeSinceLastVelCheck = 0;
                }

                // Логика переключения режимов в бою
                if (_observedTargetVelocity.LengthSquared() > _targetMovementThreshold)
                {
                    if (CurrentState != SquadState.Pursuing)
                    {
                        GD.Print($"Target is moving fast ({_observedTargetVelocity.Length():F1} m/s). Switching to PURSUIT mode.");
                        CurrentState = SquadState.Pursuing;
                    }
                }
                else
                {
                    if (CurrentState != SquadState.InCombat)
                    {
                        GD.Print($"Target has slowed down. Switching to standard COMBAT mode.");
                        CurrentState = SquadState.InCombat;
                    }
                }

                // Обновляем последнюю известную позицию, если видим цель
                if (Members.Any(m => m.GetVisibleTargetPoint(CurrentTarget).HasValue))
                {
                    _lastKnownTargetPosition = CurrentTarget.GlobalPosition;
                    //_failedRepositionAttempts = 0; // Мы уберем этот сброс здесь, чтобы не мешать логике преследования, если LoS моргнул
                }

                _repositionTimer -= delta;
                _currentRepositionCooldown -= delta;
                if (_currentRepositionCooldown > 0) return;

                bool shouldRepositionByTimer = _repositionTimer <= 0;
                bool shouldRepositionByRequest = Members.Count > 1 && _membersRequestingReposition.Count >= Members.Count * 0.6f;

                if (shouldRepositionByTimer || shouldRepositionByRequest)
                {
                    if (shouldRepositionByTimer) GD.Print($"Squad '{SquadName}' is re-evaluating positions due to timer.");
                    if (shouldRepositionByRequest) GD.Print($"Squad '{SquadName}' is re-evaluating positions due to member requests.");

                    ReEvaluateCombatPositions();
                }
            }
        }

        public void InitializeMembersFromGroup()
        {
            foreach (var node in GetTree().GetNodesInGroup(SquadName))
            {
                if (node is AIEntity ai)
                {
                    Members.Add(ai);
                    ai.AssignToSquad(this);
                }
            }
            GD.Print($"Squad '{SquadName}' initialized with {Members.Count} members.");

            if (Task == SquadTask.PatrolPath || Task == SquadTask.AssaultPath)
            {
                if (MissionPath != null && MissionPath.Curve.PointCount > 1)
                {
                    _pathPoints = MissionPath.Curve.GetBakedPoints();
                    StartFollowingPath();
                }
                else
                {
                    GD.PushWarning($"Squad '{SquadName}' is set to follow a path, but MissionPath is not assigned or has less than 2 points.");
                    Task = SquadTask.Standby;
                }
            }
        }

        private void StartFollowingPath()
        {
            GD.Print($"Squad '{SquadName}' starts/resumes following its mission path.");
            CurrentState = SquadState.FollowingPath;

            // Не сбрасываем индекс, если он уже валиден.
            // Сброс нужен только при самой первой инициализации.
            // Мы можем сделать это, проверяя, находимся ли мы уже в пути.
            // Но более надежный способ - просто не вызывать этот метод целиком для возобновления.
            // Давайте изменим логику выхода из боя (Disengage).

            // Старая логика:
            // _currentPathIndex = 0; 
            // _pathDirection = 1;

            // Новая логика: Мы просто вызываем MoveToNextWaypoint(), который использует ТЕКУЩИЙ индекс.
            MoveToNextWaypoint();
        }

        private void MoveToNextWaypoint()
        {
            // Логика определения следующей точки
            int nextPathIndex = _currentPathIndex + _pathDirection;

            if (Task == SquadTask.AssaultPath)
            {
                if (nextPathIndex >= _pathPoints.Length)
                {
                    GD.Print($"Squad '{SquadName}' has completed its assault path.");
                    CurrentState = SquadState.Idle;
                    // Отдаем приказ дойти до последней точки, разворачиваться уже некуда
                    var finalPosition = MissionPath.ToGlobal(_pathPoints[_currentPathIndex]);
                    AssignMoveTarget(finalPosition);
                    return;
                }
            }
            else // PatrolPath
            {
                if ((nextPathIndex >= _pathPoints.Length && _pathDirection > 0) || (nextPathIndex < 0 && _pathDirection < 0))
                {
                    _pathDirection *= -1; // Разворачиваемся
                    nextPathIndex = _currentPathIndex + _pathDirection;
                }
            }

            _currentPathIndex = nextPathIndex;

            // Логика ориентации формации
            // Мы определяем не только куда идти, но и куда при этом смотреть.
            var targetPosition = MissionPath.ToGlobal(_pathPoints[_currentPathIndex]);
            Vector3? lookAtPosition = null;

            int lookAtIndex = _currentPathIndex + _pathDirection;
            // Проверяем, есть ли следующая точка на пути, чтобы смотреть на нее
            if (lookAtIndex >= 0 && lookAtIndex < _pathPoints.Length)
            {
                lookAtPosition = MissionPath.ToGlobal(_pathPoints[lookAtIndex]);
            }

            // Передаем обе точки в обновленный метод
            AssignMoveTarget(targetPosition, lookAtPosition);
            CurrentState = SquadState.FollowingPath;
        }

        // Метод теперь принимает опциональную цель для взгляда
        public void AssignMoveTarget(Vector3 targetPosition, Vector3? lookAtPosition = null)
        {
            GD.Print($"Squad '{SquadName}' moving to {targetPosition}.");
            CurrentState = SquadState.MovingToPoint;
            CurrentTarget = null;
            _membersAtDestination.Clear();

            if (MarchingFormation == null || MarchingFormation.MemberPositions.Length == 0)
            {
                GD.PushWarning($"Squad '{SquadName}' has no MarchingFormation. Moving without formation.");
                foreach (var member in Members) member.ReceiveOrderMoveTo(targetPosition);
                return;
            }

            UpdateSquadCenter();

            var effectiveLookAt = lookAtPosition ?? targetPosition;
            var direction = Members.Count > 0 ? _squadCenterCache.DirectionTo(effectiveLookAt).Normalized() : Vector3.Forward;
            if (_squadCenterCache.DistanceSquaredTo(effectiveLookAt) < 1.0f)
            {
                direction = (targetPosition - _squadCenterCache).Normalized();
            }

            var rotation = Basis.LookingAt(direction, Vector3.Up);

            // Генерируем целевые точки и оптимально их распределяем
            var worldPositions = new List<Vector3>();
            for (int i = 0; i < MarchingFormation.MemberPositions.Length; i++)
            {
                if (i >= Members.Count) break;
                var localOffset = MarchingFormation.MemberPositions[i];
                var worldOffset = rotation * localOffset;
                worldPositions.Add(targetPosition + worldOffset);
            }

            var assignments = AITacticalAnalysis.GetOptimalAssignments(Members, worldPositions, targetPosition);

            foreach (var (member, position) in assignments)
            {
                member.ReceiveOrderMoveTo(position);
            }
        }


        public void AssignCombatTarget(LivingEntity target)
        {
            if (!IsInstanceValid(target))
            {
                GD.PushWarning($"Squad '{SquadName}' received an order to attack an invalid target. Order ignored.");
                if (CurrentState == SquadState.InCombat || CurrentState == SquadState.Pursuing) Disengage();
                return;
            }

            if (Task == SquadTask.AssaultPath && CurrentState == SquadState.FollowingPath)
            {
                GD.Print($"Squad '{SquadName}' is on assault task. Ignoring target {target.Name} to reach objective.");
                return;
            }

            if (CurrentTarget == target && (CurrentState == SquadState.InCombat || CurrentState == SquadState.Pursuing)) return;

            GD.Print($"Squad '{SquadName}' engaging target {target.Name}.");
            foreach (var member in Members) member.ClearOrders();

            _repositionTimer = _repositionCheckInterval;
            _membersRequestingReposition.Clear();

            // Инициализация системы слежения для новой цели
            if (CurrentTarget != target)
            {
                _lastKnownTargetPosition = target.GlobalPosition;
                _failedRepositionAttempts = 0;
                _targetPreviousPosition = target.GlobalPosition;
                _observedTargetVelocity = Vector3.Zero;
                _timeSinceLastVelCheck = 0;
            }

            CurrentTarget = target;
            CurrentState = SquadState.InCombat; // Всегда начинаем с обычного боя

            _isUsingFormationTactic = false;
            _formationLocalOffsets.Clear();

            ReEvaluateCombatPositions();
        }

        /// <summary>
        /// Вызывается членом отряда, когда он считает свою позицию неэффективной.
        /// </summary>
        public void RequestReposition(AIEntity member)
        {
            _membersRequestingReposition.Add(member);
            GD.Print($"Member {member.Name} requested reposition. Total requests: {_membersRequestingReposition.Count}");
        }

        /// <summary>
        /// Запускает полный пересчет и назначение боевых позиций для текущей цели.
        /// </summary>
        private void ReEvaluateCombatPositions()
        {
            if (!IsInstanceValid(CurrentTarget)) return;

            _membersRequestingReposition.Clear();
            _repositionTimer = _repositionCheckInterval;
            _currentRepositionCooldown = _repositionCooldown;

            // Выбираем тактику в зависимости от режима
            if (CurrentState == SquadState.Pursuing)
            {
                GD.Print("Re-evaluating in PURSUIT mode.");
                ExecutePursuitTactic();
                return;
            }

            // Сбрасываем флаг перед поиском
            _isUsingFormationTactic = false;
            _formationLocalOffsets.Clear();

            // В обычном режиме боя ищем статичные позиции
            GD.Print("Re-evaluating in standard COMBAT mode.");
            var assignments = FindBestCombatPositions(CurrentTarget);

            if (assignments != null && assignments.Count > 0)
            {
                _failedRepositionAttempts = 0;
                AssignOrdersFromDictionary(assignments);

                // <--- НОВАЯ ЛОГИКА: Если была выбрана формация, запоминаем ее структуру для вращения
                // Мы определяем это по тому, что не используем преследование или прямую атаку.
                // Этот блок сработает для CombatFormation и FiringArc
                if (CurrentState == SquadState.InCombat)
                {
                    GD.Print("Activating formation tactic. Storing offsets for dynamic orientation.");
                    _isUsingFormationTactic = true;

                    // Вычисляем "якорь" - центр назначенных позиций
                    _squadAnchorPoint = Vector3.Zero;
                    foreach (var pos in assignments.Values) _squadAnchorPoint += pos;
                    _squadAnchorPoint /= assignments.Count;

                    // Вычисляем и сохраняем локальные смещения для каждого бойца
                    foreach (var (ai, worldPos) in assignments)
                    {
                        _formationLocalOffsets[ai] = worldPos - _squadAnchorPoint;
                    }
                }
            }
            else
            {
                _failedRepositionAttempts++;
                GD.PushWarning($"Failed to find static positions, attempt #{_failedRepositionAttempts}.");

                if (_failedRepositionAttempts >= 2)
                {
                    GD.PushError("Too many failed attempts in combat mode. Forcing pursuit tactic.");
                    ExecutePursuitTactic();
                }
                else
                {
                    ExecuteDirectAssaultTactic();
                }
            }
        }

        /// <summary>
        /// Быстро вращает текущую формацию вслед за целью.
        /// </summary>
        private void UpdateFormationOrientation()
        {
            if (!IsInstanceValid(CurrentTarget) || _formationLocalOffsets.Count == 0)
            {
                _isUsingFormationTactic = false;
                return;
            }

            GD.Print("Updating formation orientation...");

            // 1. Определяем новое направление для "взгляда" формации
            var directionToTarget = (_squadAnchorPoint.DirectionTo(CurrentTarget.GlobalPosition)).Normalized();
            if (directionToTarget.IsZeroApprox()) return; // Избегаем ошибок, если цель в центре якоря

            var newRotation = Basis.LookingAt(directionToTarget, Vector3.Up);

            // 2. Пересчитываем и отдаем новые приказы на движение
            foreach (var (ai, localOffset) in _formationLocalOffsets)
            {
                // Вращаем сохраненное локальное смещение
                var rotatedOffset = newRotation * localOffset;
                // Находим новую глобальную позицию
                var newTargetPosition = _squadAnchorPoint + rotatedOffset;

                // Отдаем "мягкий" приказ на движение. AI плавно скорректирует свой путь.
                ai.ReceiveOrderMoveTo(newTargetPosition);
            }
        }

        /// <summary>
        /// Вызывается членом отряда, когда его цель становится невалидной.
        /// Позволяет командиру отряда принять решение о дальнейших действиях.
        /// </summary>
        public void ReportTargetEliminated(AIEntity reporter, LivingEntity eliminatedTarget)
        {
            // 1. Проверяем, является ли этот доклад актуальным.
            // Если цель уже не та, которую мы атакуем, или доклад пришел с опозданием, игнорируем.
            if (!IsInstanceValid(CurrentTarget) || eliminatedTarget != CurrentTarget)
            {
                return;
            }

            GD.Print($"Squad '{SquadName}' confirms target {eliminatedTarget.Name} is eliminated. Disengaging...");

            // 2. Выводим отряд из боя и возвращаем его к основной задаче.
            Disengage();
        }

        /// <summary>
        /// Выводит отряд из состояния боя, отменяет приказы и, если нужно, возвращает к патрулированию.
        /// </summary>
        private void Disengage()
        {
            CurrentTarget = null;
            CurrentState = SquadState.Idle; // Переходим в безопасное состояние

            foreach (var member in Members)
            {
                member.ClearOrders();
            }

            if (Task == SquadTask.PatrolPath || Task == SquadTask.AssaultPath)
            {
                // <--- ИЗМЕНЕНИЕ: Вместо полного перезапуска патруля, мы просто продолжаем с того места, где остановились.
                GD.Print($"Squad '{SquadName}' resuming mission path after combat.");
                // StartFollowingPath(); // Старый вызов, который все сбрасывал

                // Новый подход: Просто даем команду двигаться к текущей (или следующей) точке маршрута.
                // Это предотвратит бег назад.
                CurrentState = SquadState.FollowingPath;
                MoveToNextWaypoint();
            }
        }

        public void ReportPositionReached(AIEntity member)
        {
            if (CurrentState != SquadState.MovingToPoint && CurrentState != SquadState.FollowingPath) return;

            _membersAtDestination.Add(member);
            if (CurrentState == SquadState.MovingToPoint && _membersAtDestination.Count >= Members.Count)
            {
                GD.Print($"Squad '{SquadName}' has reached its destination. Switching to Idle.");
                CurrentState = SquadState.Idle;
            }
        }

        public void OnMemberDestroyed(AIEntity member)
        {
            Members.Remove(member);
            _membersAtDestination.Remove(member);
            if (Members.Count == 0)
            {
                GD.Print($"Squad '{SquadName}' has been eliminated.");
                QueueFree();
            }
            else if (CurrentState == SquadState.InCombat && IsInstanceValid(CurrentTarget))
            {
                // Переназначаем боевые позиции с учетом потерь
                AssignCombatTarget(CurrentTarget);
            }
        }

        private void UpdateSquadCenter()
        {
            if (Members.Count == 0) return;
            _squadCenterCache = Members.Select(m => m.GlobalPosition).Aggregate(Vector3.Zero, (a, b) => a + b) / Members.Count;
        }

        #region Utils

        private void ExecutePursuitTactic()
        {
            Vector3 pursuitPoint = _lastKnownTargetPosition + (_observedTargetVelocity * _pursuitPredictionTime);
            GD.Print($"Pursuit prediction: To {pursuitPoint} using observed velocity {_observedTargetVelocity.Length()} m/s");

            var assignments = new Dictionary<AIEntity, Vector3>();
            foreach (var member in Members)
            {
                float engagementDistance = member.CombatBehavior.AttackRange * member.Profile.CombatProfile.EngagementRangeFactor;
                Vector3 directionFromPursuitPoint = (member.GlobalPosition - pursuitPoint).Normalized();
                if (directionFromPursuitPoint.IsZeroApprox())
                    directionFromPursuitPoint = Vector3.Forward.Rotated(Vector3.Up, (float)GD.RandRange(0, Mathf.Pi * 2));

                assignments[member] = pursuitPoint + directionFromPursuitPoint * engagementDistance;
            }
            AssignOrdersFromDictionary(assignments);
        }

        private void ExecuteDirectAssaultTactic()
        {
            GD.PushWarning("Executing direct assault.");
            var assignments = new Dictionary<AIEntity, Vector3>();
            foreach (var member in Members)
            {
                assignments[member] = CurrentTarget.GlobalPosition;
            }
            AssignOrdersFromDictionary(assignments);
        }

        private Dictionary<AIEntity, Vector3> FindBestCombatPositions(LivingEntity target)
        {
            var representative = Members.FirstOrDefault(m => IsInstanceValid(m) && m.CombatBehavior?.Action?.MuzzlePoint != null);
            Vector3 muzzleOffset = representative?.CombatBehavior.Action.MuzzlePoint.Position ?? Vector3.Zero;

            var assignments = AITacticalAnalysis.FindCoverAndFirePositions(Members, target, muzzleOffset);
            if (assignments == null || assignments.Count < Members.Count)
            {
                assignments = AITacticalAnalysis.GeneratePositionsFromFormation(Members, CombatFormation, target, muzzleOffset);
            }
            if (assignments == null || assignments.Count < Members.Count)
            {
                var arcPositions = AITacticalAnalysis.GenerateFiringArcPositions(Members, target);
                if (arcPositions != null && arcPositions.Count > 0)
                {
                    assignments = AITacticalAnalysis.GetOptimalAssignments(Members, arcPositions, target.GlobalPosition);
                }
            }
            return assignments;
        }

        private void AssignOrdersFromDictionary(Dictionary<AIEntity, Vector3> assignments)
        {
            foreach (var (ai, position) in assignments)
            {
                ai.ReceiveOrderMoveTo(position);
                ai.ReceiveOrderAttackTarget(CurrentTarget);
            }

            // Назначаем приказ тем, кому позиция не досталась
            foreach (var member in Members.Where(m => !assignments.ContainsKey(m)))
            {
                GD.PushWarning($"Member {member.Name} did not receive a position, ordering direct assault.");
                member.ReceiveOrderMoveTo(CurrentTarget.GlobalPosition);
                member.ReceiveOrderAttackTarget(CurrentTarget);
            }
        }

        #endregion
    }
}