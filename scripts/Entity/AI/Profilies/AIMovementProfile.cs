using Godot;

namespace Game.Entity.AI.Profiles
{
    [GlobalClass]
    public partial class AIMovementProfile : Resource
    {
        [ExportGroup("Movement Speeds")]
        [Export] public float SlowSpeed { get; private set; } = 3.0f;
        [Export] public float NormalSpeed { get; private set; } = 5.0f;
        [Export] public float FastSpeed { get; private set; } = 8.0f;

        [ExportGroup("Rotation Speeds")]
        [Export(PropertyHint.Range, "1, 20, 0.5")] public float BodyRotationSpeed { get; private set; } = 10f;
        [Export(PropertyHint.Range, "1, 20, 0.5")] public float HeadRotationSpeed { get; private set; } = 15f;
    }
}