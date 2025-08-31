using Godot;
using System.Collections.Generic;
using System.Linq;
using Game.Entity.AI.Orchestrator;

namespace Game.Entity.AI.Components
{
    public enum SquadState { Idle, MovingToPoint, FollowingPath, InCombat }
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
        [Export] private float _pathWaypointThreshold = 2.0f;

        public readonly List<AIEntity> Members = [];
        public SquadState CurrentState { get; private set; } = SquadState.Idle;
        public LivingEntity CurrentTarget { get; private set; }

        private readonly HashSet<AIEntity> _membersAtDestination = [];
        private Vector3 _squadCenterCache;

        private Vector3[] _pathPoints;
        private int _currentPathIndex = 0;
        private int _pathDirection = 1;

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
            // В состоянии боя командир теперь действует, только когда получает доклад от бойцов,
            // поэтому активная проверка цели здесь больше не нужна. Это делает систему более событийно-ориентированной.
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
                if (CurrentState == SquadState.InCombat) Disengage();
                return;
            }

            if (Task == SquadTask.AssaultPath && CurrentState == SquadState.FollowingPath)
            {
                GD.Print($"Squad '{SquadName}' is on assault task. Ignoring target {target.Name} to reach objective.");
                return;
            }

            if (CurrentTarget == target && CurrentState == SquadState.InCombat) return;

            GD.Print($"Squad '{SquadName}' engaging target {target.Name}.");

            foreach (var member in Members) member.ClearOrders();

            CurrentTarget = target;
            CurrentState = SquadState.InCombat;
            _membersAtDestination.Clear();

            // <--- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Вычисляем смещение дула перед анализом.
            var representative = Members.FirstOrDefault(m => IsInstanceValid(m) && m.CombatBehavior?.Action?.MuzzlePoint != null);
            Vector3 muzzleOffset = Vector3.Zero;
            if (representative != null)
            {
                // Мы берем локальную позицию MuzzlePoint. Когда мы прибавляем ее к глобальной
                // позиции на NavMesh, мы эффективно симулируем глобальное положение дула.
                muzzleOffset = representative.CombatBehavior.Action.MuzzlePoint.Position;
            }
            else
            {
                GD.PushWarning($"Squad '{SquadName}' has no members with a MuzzlePoint. Line-of-fire checks may be inaccurate.");
                // В качестве запасного варианта можно использовать высоту глаз.
                // muzzleOffset = Vector3.Up * 1.6f;
            }

            Dictionary<AIEntity, Vector3> positionAssignments = AITacticalAnalysis.FindCoverAndFirePositions(Members, target, muzzleOffset);

            // Если с укрытиями не вышло, пробуем боевую формацию.
            if (positionAssignments == null || positionAssignments.Count < Members.Count)
            {
                GD.Print($"Squad '{SquadName}' failed to find enough cover positions. Using CombatFormation as fallback.");
                positionAssignments = AITacticalAnalysis.GeneratePositionsFromFormation(Members, CombatFormation, target, muzzleOffset);
            }

            // Если и с формацией не вышло, пробуем План В: создать огневую дугу.
            if (positionAssignments == null || positionAssignments.Count < Members.Count)
            {
                GD.PushWarning($"Squad '{SquadName}' failed to find formation positions. Attempting to generate a firing arc.");

                // <--- ИЗМЕНЕНИЕ: Используем новую версию GenerateFiringArcPositions ---
                var arcPositions = AITacticalAnalysis.GenerateFiringArcPositions(Members, target);

                if (arcPositions != null && arcPositions.Count > 0)
                {
                    // Используем наш "умный" распределитель
                    positionAssignments = AITacticalAnalysis.GetOptimalAssignments(Members, arcPositions, target.GlobalPosition);
                }
            }

            // Если после всех попыток позиций все равно нет или их мало, переходим к Плану Г.
            if (positionAssignments == null || positionAssignments.Count == 0)
            {
                GD.PushError($"Squad '{SquadName}' could not find/generate any valid positions. Ordering a direct assault!");
                foreach (var member in Members)
                {
                    member.ReceiveOrderMoveTo(target.GlobalPosition);
                    member.ReceiveOrderAttackTarget(target);
                }
                return; // Выходим
            }

            // --- Блок назначения приказов (остается почти без изменений) ---
            GD.Print($"Assigning {positionAssignments.Count} combat positions.");
            foreach (var (ai, position) in positionAssignments)
            {
                ai.ReceiveOrderMoveTo(position);
                ai.ReceiveOrderAttackTarget(target);
            }

            // Назначаем приказ на прямую атаку тем, кому позиция не досталась
            foreach (var member in Members.Where(m => !positionAssignments.ContainsKey(m)))
            {
                GD.Print($"Member {member.Name} did not receive a position, ordering direct assault.");
                member.ReceiveOrderMoveTo(target.GlobalPosition);
                member.ReceiveOrderAttackTarget(target);
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
    }
}