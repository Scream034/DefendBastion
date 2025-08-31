using Godot;

namespace Game.Entity.AI.Profiles
{
    [GlobalClass]
    public partial class AICombatProfile : Resource
    {
        [ExportGroup("Targeting System")]
        [Export(PropertyHint.Range, "0.1, 2.0, 0.1")]
        public float TargetEvaluationInterval = 0.5f;

        [ExportGroup("Repositioning")]
        [Export(PropertyHint.Range, "0.5, 5.0, 0.1")] public float RepositionSearchStep { get; private set; } = 1.5f;

        [ExportGroup("Shooting Accuracy")]
        [Export(PropertyHint.Range, "0.0, 10.0, 0.1")]
        public float AimSpreadRadius { get; private set; } = 2.25f;
        [Export]
        public bool EnableTargetPrediction { get; private set; } = true;
        [Export(PropertyHint.Range, "0.0, 1.0, 0.05")]
        public float PredictionAccuracy { get; private set; } = 0.7f;

        [ExportGroup("Line of Sight")]
        [Export(PropertyHint.Layers3DPhysics)]
        public uint LineOfSightMask { get; private set; } = 3; // Layer 1 (World) + Layer 2 (Entity)

        // Добавляем коэффициент для расчета оптимальной дистанции.
        // 0.7 означает, что AI будет стремиться подойти на 70% от своей максимальной дальности атаки.
        // Для дробовиков можно поставить 0.4, для винтовок 0.8.
        [Export(PropertyHint.Range, "0.1,1.0")]
        public float EngagementRangeFactor { get; private set; } = 0.65f;

        [ExportGroup("Post-Combat Behavior")]
        [Export] public bool EnablePostCombatVigilance { get; private set; } = true;
        [Export(PropertyHint.Range, "3, 15, 1")] public float VigilanceDuration { get; private set; } = 8.0f;
        [Export] public bool AllowVigilanceStrafe { get; private set; } = true;
    }
}