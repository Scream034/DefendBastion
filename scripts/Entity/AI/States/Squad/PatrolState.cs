// Game/Entity/AI/States/Squad/PatrolState.cs
using Godot;
using System.Collections.Generic;
using Game.Entity.AI.Components;

namespace Game.Entity.AI.States.Squad
{
    public class PatrolState(AISquad squad) : SquadStateBase(squad)
    {
        private Vector3 _currentTargetWaypointGlobal;

        public override void Enter()
        {
            GD.Print($"Squad '{Squad.SquadName}' starts/resumes following its mission path.");
            Squad.CurrentTarget = null;
            // Сразу же даем команду двигаться к текущей (или следующей) точке,
            // чтобы не было задержки.
            MoveToNextWaypoint();
        }

        public override void Process(double delta)
        {
            if (Squad.Members.Count == 0) return;

            var squadCenter = Squad.GetSquadCenter();

            if (squadCenter.DistanceSquaredTo(_currentTargetWaypointGlobal) < Squad.PathWaypointThreshold * Squad.PathWaypointThreshold)
            {
                MoveToNextWaypoint();
            }
        }

        private void MoveToNextWaypoint()
        {
            int nextPathIndex = Squad.CurrentPathIndex + Squad.PathDirection;

            // Логика для штурмового пути (AssaultPath)
            if (Squad.Task == SquadTask.AssaultPath)
            {
                if (nextPathIndex >= Squad.PathPoints.Length)
                {
                    GD.Print($"Squad '{Squad.SquadName}' has completed its assault path.");
                    // Отдаем финальный приказ и переходим в состояние ожидания
                    var finalPosition = Squad.MissionPath.ToGlobal(Squad.PathPoints[^1]); // ^1 = последний элемент
                    Squad.ChangeState(new MoveToPointState(Squad, finalPosition, null));
                    return;
                }
            }
            // Логика для циклического патруля (PatrolPath)
            else
            {
                if ((nextPathIndex >= Squad.PathPoints.Length && Squad.PathDirection > 0) || (nextPathIndex < 0 && Squad.PathDirection < 0))
                {
                    Squad.PathDirection *= -1; // Разворачиваемся
                    nextPathIndex = Squad.CurrentPathIndex + Squad.PathDirection;
                }
            }

            Squad.CurrentPathIndex = nextPathIndex;

            // Определяем цель движения и цель для взгляда (следующая точка на пути)
            _currentTargetWaypointGlobal = Squad.MissionPath.ToGlobal(Squad.PathPoints[Squad.CurrentPathIndex]);
            Vector3? lookAtPosition = null;

            int lookAtIndex = Squad.CurrentPathIndex + Squad.PathDirection;
            if (lookAtIndex >= 0 && lookAtIndex < Squad.PathPoints.Length)
            {
                lookAtPosition = Squad.MissionPath.ToGlobal(Squad.PathPoints[lookAtIndex]);
            }

            // Отдаем приказ на движение в формации
            Squad.AssignMarchingFormationMove(_currentTargetWaypointGlobal, lookAtPosition);
        }
    }
}