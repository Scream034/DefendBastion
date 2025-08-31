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
            // <--- ИЗМЕНЕНИЕ: Добавляем блок управления состоянием боя ---
            else if (CurrentState == SquadState.InCombat)
            {
                // Командир отряда сам проверяет, актуальна ли его цель.
                if (!IsInstanceValid(CurrentTarget) || !CurrentTarget.IsAlive)
                {
                    GD.Print($"Squad '{SquadName}' target is eliminated or invalid. Disengaging.");
                    Disengage();
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
            GD.Print($"Squad '{SquadName}' starts following its mission path.");
            CurrentState = SquadState.FollowingPath;
            _currentPathIndex = 0;
            _pathDirection = 1; // Всегда начинаем движение вперед по пути
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

            var assignments = GetOptimalAssignments(Members, worldPositions);

            foreach (var (member, position) in assignments)
            {
                member.ReceiveOrderMoveTo(position);
            }
        }

        public void AssignCombatTarget(LivingEntity target)
        {
            // <--- ИЗМЕНЕНИЕ: "Пуленепробиваемый" вход в метод ---
            // Это первая линия обороны. Ни при каких обстоятельствах мы не работаем с невалидной целью.
            if (!IsInstanceValid(target))
            {
                GD.PushWarning($"Squad '{SquadName}' received an order to attack an invalid target. Order ignored.");
                // Если мы уже были в бою, то отменяем его, т.к. пришел приказ на невалидную цель
                if (CurrentState == SquadState.InCombat)
                {
                    Disengage();
                }
                return;
            }

            if (Task == SquadTask.AssaultPath && CurrentState == SquadState.FollowingPath)
            {
                GD.Print($"Squad '{SquadName}' is on assault task. Ignoring target {target.Name} to reach objective.");
                return;
            }

            // Если мы уже атакуем эту же цель, ничего не делаем.
            if (CurrentTarget == target && CurrentState == SquadState.InCombat) return;

            GD.Print($"Squad '{SquadName}' engaging target {target.Name}.");

            // Очищаем предыдущие приказы, чтобы не мешать новому
            foreach (var member in Members)
            {
                member.ClearOrders();
            }

            CurrentTarget = target;
            CurrentState = SquadState.InCombat;
            _membersAtDestination.Clear();

            var positionAssignments = AITacticalAnalysis.FindCoverAndFirePositions(Members, target);

            if (positionAssignments == null || positionAssignments.Count == 0)
            {
                GD.Print($"Squad '{SquadName}' failed to find any cover. Using CombatFormation as fallback.");
                positionAssignments = GeneratePositionsFromFormation(CombatFormation, target);
            }

            if (positionAssignments != null && positionAssignments.Count > 0)
            {
                GD.Print($"Assigning {positionAssignments.Count} combat positions.");
                foreach (var (ai, position) in positionAssignments)
                {
                    ai.ReceiveOrderMoveTo(position);
                    ai.ReceiveOrderAttackTarget(target);
                }
            }
            else
            {
                // Эта ошибка теперь будет вызываться только в том случае, если цель валидна,
                // но по какой-то причине мы не смогли сгенерировать для нее НИ ОДНОЙ позиции.
                GD.PushError($"Squad '{SquadName}' could not determine any combat positions for target {target.Name}!");
                // В этом случае отряд должен прекратить попытки, а не зацикливаться.
                Disengage();
            }
        }

        // <--- ИЗМЕНЕНИЕ: Новый метод для чистого выхода из боя ---
        /// <summary>
        /// Выводит отряд из состояния боя, отменяет приказы и переводит в режим ожидания.
        /// </summary>
        private void Disengage()
        {
            CurrentTarget = null;
            CurrentState = SquadState.Idle; // Переходим в безопасное состояние

            foreach (var member in Members)
            {
                member.ClearOrders();
            }

            // TODO: В будущем здесь можно добавить логику возвращения к патрулированию,
            // но пока что простой переход в Idle - самое надежное решение.
        }

        private Dictionary<AIEntity, Vector3> GeneratePositionsFromFormation(Formation formation, LivingEntity target)
        {
            if (formation == null || formation.MemberPositions.Length == 0 || !IsInstanceValid(target)) return null;

            UpdateSquadCenter();

            float optimalDistance = Members.Average(ai => ai.CombatBehavior.AttackRange) * 0.8f;
            var directionFromTarget = target.GlobalPosition.DirectionTo(_squadCenterCache).Normalized();
            var anchorPoint = target.GlobalPosition + directionFromTarget * optimalDistance;

            var lookDirection = anchorPoint.DirectionTo(target.GlobalPosition).Normalized();
            var rotation = Basis.LookingAt(lookDirection, Vector3.Up);

            // <--- ИЗМЕНЕНИЕ: Теперь мы ищем не просто точки, а ТАКТИЧЕСКИ ВЫГОДНЫЕ точки ---
            var validCombatPositions = new List<Vector3>();
            var navMap = Members[0].GetWorld3D().NavigationMap;
            uint losMask = Members[0].Profile.CombatProfile.LineOfSightMask;
            // Собираем RID'ы отряда один раз, чтобы не делать это в цикле
            var squadRids = new Godot.Collections.Array<Rid>();
            foreach (var member in Members) squadRids.Add(member.GetRid());
            squadRids.Add(target.GetRid());

            for (int i = 0; i < formation.MemberPositions.Length; i++)
            {
                var localOffset = formation.MemberPositions[i];
                var worldOffset = rotation * localOffset;
                var idealPosition = anchorPoint + worldOffset;
                var navMeshPosition = NavigationServer3D.MapGetClosestPoint(navMap, idealPosition);

                // Проверяем, что точка на NavMesh не слишком далеко от идеальной
                if (navMeshPosition.DistanceSquaredTo(idealPosition) > 4.0f) // допуск 2 метра
                {
                    continue; // Эта точка не подходит, ищем для следующего бойца
                }

                // "есть ли точка, которую можно увидеть?".
                if (AITacticalAnalysis.GetFirstVisiblePointOfTarget(navMeshPosition, target, squadRids, losMask).HasValue)
                {
                    validCombatPositions.Add(navMeshPosition);
                }
            }

            // Если мы не смогли найти достаточно хороших позиций для всего отряда, считаем операцию провальной.
            if (validCombatPositions.Count < Members.Count)
            {
                GD.Print($"Squad '{SquadName}' found only {validCombatPositions.Count} valid combat positions out of {Members.Count} required. Fallback failed.");
                return null;
            }

            // Теперь, когда у нас есть список гарантированно хороших позиций, оптимально распределяем их.
            return GetOptimalAssignments(Members, validCombatPositions);
        }

        /// <summary>
        /// Реализует жадный алгоритм для назначения агентам ближайших к ним целевых позиций.
        /// Минимизирует общее расстояние перемещения отряда.
        /// </summary>
        /// <param name="agents">Список агентов для назначения.</param>
        /// <param name="positions">Список доступных позиций.</param>
        /// <returns>Словарь оптимальных назначений {Агент -> Позиция}.</returns>
        private static Dictionary<AIEntity, Vector3> GetOptimalAssignments(List<AIEntity> agents, List<Vector3> positions)
        {
            var assignments = new Dictionary<AIEntity, Vector3>();
            var unassignedAgents = new List<AIEntity>(agents);
            var availablePositions = new List<Vector3>(positions);

            foreach (var position in availablePositions)
            {
                if (unassignedAgents.Count == 0) break;

                // Находим ближайшего к этой позиции свободного агента
                AIEntity bestAgent = unassignedAgents
                    .OrderBy(agent => agent.GlobalPosition.DistanceSquaredTo(position))
                    .First();

                assignments[bestAgent] = position;
                unassignedAgents.Remove(bestAgent);
            }

            return assignments;
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