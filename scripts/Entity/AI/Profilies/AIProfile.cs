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
        [ExportGroup("AI Mission")]
        [Export] public AIMainTask MainTask { get; private set; } = AIMainTask.FreePatrol;
        [Export] public AssaultMode AssaultBehavior { get; private set; } = AssaultMode.Destroy;
        [Export] public NodePath MissionNodePath { get; private set; }
        public Path3D MissionPath { get; private set; }

        [ExportGroup("AI Profiles")]
        [Export] public AIMovementProfile MovementProfile { get; private set; }
        [Export] public AIPatrolProfile PatrolProfile { get; private set; }
        [Export] public AICombatProfile CombatProfile { get; private set; }
        [Export] public AILookProfile LookProfile { get; private set; }

        public void Initialize(AIEntity entity)
        {
            MissionPath = entity.GetNodeOrNull<Path3D>(MissionNodePath);
        }
    }
}