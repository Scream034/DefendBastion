namespace Game.Entity.AI
{
    public static class FactionManager
    {
        public static bool AreFactionsHostile(Faction f1, Faction f2)
        {
            if (f1 == Faction.Neutral || f2 == Faction.Neutral)
            {
                return false;
            }

            // Entities of the same faction are not hostile to each other
            if (f1 == f2)
            {
                return false;
            }

            // Define hostility rules here
            // For now, Player and Enemy are hostile to each other.
            if ((f1 == Faction.Player && f2 == Faction.Enemy) || (f1 == Faction.Enemy && f2 == Faction.Player))
            {
                return true;
            }

            // Default case: no hostility
            return false;
        }
    }
}
