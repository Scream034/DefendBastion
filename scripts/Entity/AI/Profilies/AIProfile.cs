using Godot;

namespace Game.Entity.AI.Profiles
{
    /// <summary>
    /// Главный ресурс, объединяющий все конфигурационные профили для AIEntity.
    /// Позволяет легко настраивать и переиспользовать полные наборы поведений ИИ.
    /// </summary>
    [GlobalClass]
    public partial class AIProfile : Resource
    {
        [ExportGroup("AI Profiles")]
        [Export] public AIMovementProfile MovementProfile { get; private set; }
        [Export] public AIPatrolProfile PatrolProfile { get; private set; }
        [Export] public AICombatProfile CombatProfile { get; private set; }
        [Export] public AILookProfile LookProfile { get; private set; }
    }
}