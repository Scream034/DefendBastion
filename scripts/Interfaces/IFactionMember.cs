using Game.Entity.AI;

namespace Game.Interfaces
{
    public interface IFactionMember
    {
        Faction Faction { get; set; }

        bool IsHostile(IFactionMember other);
    }
}
