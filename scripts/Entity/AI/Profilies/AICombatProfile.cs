using Godot;

namespace Game.Entity.AI.Profiles
{
    [GlobalClass]
    public partial class AICombatProfile : Resource
    {
        [ExportGroup("Targetting System")]
        [Export(PropertyHint.Range, "0.1, 2.0, 0.1")]
        public float TargetEvaluationInterval = 0.5f;

        [ExportGroup("Repositioning")]
        [Export(PropertyHint.Range, "0.5, 5.0, 0.1")] public float RepositionSearchStep { get; private set; } = 1.5f;

        [ExportGroup("Post-Combat Behavior")]
        [Export] public bool EnablePostCombatVigilance { get; private set; } = true;
        [Export(PropertyHint.Range, "3, 15, 1")] public float VigilanceDuration { get; private set; } = 8.0f;
        [Export] public bool AllowVigilanceStrafe { get; private set; } = true;
    }
}