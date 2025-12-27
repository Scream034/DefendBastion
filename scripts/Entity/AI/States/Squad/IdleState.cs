using Godot;

namespace Game.Entity.AI.States.Squad
{
    public class IdleState(Components.AISquad squad) : SquadStateBase(squad)
    {
        public override void Enter()
        {
            GD.Print($"Squad '{Squad.Name}' is now in Idle state.");
            foreach (var member in Squad.Members)
            {
                member.ClearOrders();
            }
        }
    }
}