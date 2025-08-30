using Godot;

namespace Game.Entity.AI.Profiles
{
    [GlobalClass]
    public partial class AIPatrolProfile : Resource
    {
        [ExportGroup("Radius Settings")]
        [Export] public bool UseRandomPatrolRadius { get; private set; } = true;
        [Export] public float MinPatrolRadius { get; private set; } = 5f;
        [Export] public float MaxPatrolRadius { get; private set; } = 20f;

        [ExportGroup("Wait Time Settings")]
        [Export] public bool UseRandomWaitTime { get; private set; } = true;
        [Export(PropertyHint.Range, "0, 10, 0.5")] public float MinPatrolWaitTime { get; private set; } = 1.0f;
        [Export(PropertyHint.Range, "0, 10, 0.5")] public float MaxPatrolWaitTime { get; private set; } = 3.0f;
    }
}