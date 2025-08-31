using Godot;

namespace Game.Entity.AI.Orchestrator
{
    /// <summary>
    /// Ресурс для определения тактического построения отряда.
    /// Позиции задаются локально относительно центра/лидера отряда.
    /// </summary>
    [GlobalClass]
    public partial class Formation : Resource
    {
        [Export] public Vector3[] MemberPositions { get; private set; } = 
        {
            // По умолчанию - клин из 3-х юнитов
            Vector3.Zero, 
            new(-2, 0, -2), 
            new(2, 0, -2) 
        };
    }
}