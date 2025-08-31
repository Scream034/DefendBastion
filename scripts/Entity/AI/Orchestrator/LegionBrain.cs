using Godot;
using System.Collections.Generic;
using Game.Entity.AI.Components;

namespace Game.Entity.AI.Orchestrator
{
    public partial class LegionBrain : Node
    {
        public static LegionBrain Instance { get; private set; }

        private readonly Dictionary<string, AISquad> _squads = [];

        public override void _EnterTree() => Instance = this;

        public override void _Ready()
        {
            GetTree().CreateTimer(0.1).Timeout += FinalizeSquadInitialization;
        }

        public void RegisterSquad(AISquad squad)
        {
            if (!_squads.ContainsKey(squad.SquadName))
            {
                _squads[squad.SquadName] = squad;
                GD.Print($"Legion Brain: Squad '{squad.SquadName}' registered.");
            }
        }

        private void FinalizeSquadInitialization()
        {
            foreach (var squad in _squads.Values)
            {
                squad.InitializeMembersFromGroup();
            }
        }

        public void ReportEnemySighting(AIEntity reporter, LivingEntity target)
        {
            if (reporter.Squad == null) return;
            reporter.Squad.AssignCombatTarget(target);
        }

        /// <summary>
        /// Отдает приказ указанному отряду двигаться к цели.
        /// </summary>
        public void OrderSquadToMove(string squadName, Vector3 targetPosition)
        {
            if (_squads.TryGetValue(squadName, out var squad))
            {
                squad.AssignMoveTarget(targetPosition);
            }
            else
            {
                GD.PushWarning($"Legion Brain: Attempted to order non-existent squad '{squadName}'.");
            }
        }
    }
}