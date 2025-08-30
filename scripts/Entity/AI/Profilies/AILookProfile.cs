using Godot;

namespace Game.Entity.AI.Profiles
{
    [GlobalClass]
    public partial class AILookProfile : Resource
    {
        [ExportGroup("Glance Behavior")]
        [Export] public bool LookAtInterestPointWhileMoving { get; private set; } = true;
        [Export(PropertyHint.Range, "0.5, 3.0, 0.1")] public float MinGlanceDuration { get; private set; } = 1.0f;
        [Export(PropertyHint.Range, "1.0, 4.0, 0.1")] public float MaxGlanceDuration { get; private set; } = 2.5f;
        [Export(PropertyHint.Range, "0.5, 3.0, 0.1")] public float MinLookForwardDuration { get; private set; } = 0.8f;
        [Export(PropertyHint.Range, "1.0, 4.0, 0.1")] public float MaxLookForwardDuration { get; private set; } = 2.0f;
        
        [ExportGroup("Rotation Limits")]
        [Export] public bool EnableHeadRotationLimits { get; private set; } = true;
        [Export(PropertyHint.Range, "0, 180, 1")] public float MaxHeadYawDegrees { get; private set; } = 90f;
        [Export(PropertyHint.Range, "0, 90, 1")] public float MaxHeadPitchDegrees { get; private set; } = 60f;
    }
}