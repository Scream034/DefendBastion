using Godot;

namespace Game.Entity.AI.Profiles
{
    /// <summary>
    /// Профиль настроек взгляда, включающий приоритеты, ограничения и параметры "оживлени".
    /// </summary>
    [GlobalClass]
    public partial class AILookProfile : Resource
    {
        [ExportGroup("Priorities")]
        [Export(PropertyHint.Range, "0, 100, 1")] public int PriorityManualOverride { get; private set; } = 100;
        [Export(PropertyHint.Range, "0, 100, 1")] public int PriorityCombat { get; private set; } = 80;
        [Export(PropertyHint.Range, "0, 100, 1")] public int PriorityDamageReaction { get; private set; } = 60;
        [Export(PropertyHint.Range, "0, 100, 1")] public int PriorityInvestigation { get; private set; } = 40;
        [Export(PropertyHint.Range, "0, 100, 1")] public int PriorityCasual { get; private set; } = 20;

        [ExportGroup("Organic Feel")]
        /// <summary>
        /// Скорость сглаживания движения головы (Lerp). Чем меньше, тем "тяжелее" и плавнее голова.
        /// Высокие значения (15+) делают движения резкими/роботизированными.
        /// </summary>
        [Export(PropertyHint.Range, "1.0, 20.0, 0.5")]
        public float LookSmoothingSpeed { get; private set; } = 6.0f;

        /// <summary>
        /// Амплитуда микро-движений (шума) в метрах.
        /// </summary>
        [Export(PropertyHint.Range, "0.0, 1.0, 0.05")]
        public float NoiseAmplitude { get; private set; } = 0.2f;

        /// <summary>
        /// Скорость изменения шума. Выше = "нервный" взгляд.
        /// </summary>
        [Export(PropertyHint.Range, "0.1, 5.0, 0.1")]
        public float NoiseFrequency { get; private set; } = 1.0f;

        [ExportGroup("Glance Behavior")]
        [Export] public bool LookAtInterestPointWhileMoving { get; private set; } = true;
        [Export(PropertyHint.Range, "0.5, 3.0, 0.1")] public float MinGlanceDuration { get; private set; } = 1.0f;
        [Export(PropertyHint.Range, "1.0, 4.0, 0.1")] public float MaxGlanceDuration { get; private set; } = 2.5f;

        [ExportGroup("Rotation Limits")]
        [Export] public bool EnableHeadRotationLimits { get; private set; } = true;
        [Export(PropertyHint.Range, "0, 180, 1")] public float MaxHeadYawDegrees { get; private set; } = 90f;
        [Export(PropertyHint.Range, "0, 90, 1")] public float MaxHeadPitchDegrees { get; private set; } = 60f;
    }
}