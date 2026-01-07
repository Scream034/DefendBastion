using Godot;

namespace Game.Entity.AI.States.Squad
{
    public class MoveToPointState(Components.AISquad squad, Vector3 targetPosition, Vector3? lookAtPosition)
        : SquadStateBase(squad)
    {
        public override void Enter()
        {
            GD.Print($"Squad '{Squad.Name}' moving to {targetPosition}.");
            Squad.AssignMarchingFormationMove(targetPosition, lookAtPosition);
        }

        public override void Process(double delta)
        {
            if (Squad.MembersAtDestination.Count >= Squad.Members.Count)
            {
                GD.Print($"Squad '{Squad.Name}' has reached its destination. Switching to Idle.");
                Squad.ChangeState(new IdleState(Squad));
            }
        }
    }
}