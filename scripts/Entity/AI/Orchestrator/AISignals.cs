using Godot;

namespace Game.Entity.AI.Orchestrator
{
    /// <summary>
    /// Глобальный хаб для событий, связанных с AI.
    /// Позволяет разным системам (AIEntity, AISquad, LegionBrain) общаться,
    /// не имея прямых ссылок друг на друга.
    /// </summary>
    public partial class AISignals : Node
    {
        public static AISignals Instance { get; private set; }

        public override void _EnterTree() => Instance = this;

        /// <summary>
        /// Боец сообщает об обнаружении противника.
        /// </summary>
        [Signal]
        public delegate void EnemySightedEventHandler(AIEntity reporter, LivingEntity target);

        /// <summary>
        /// Боец сообщает, что его цель уничтожена.
        /// </summary>
        [Signal]
        public delegate void TargetEliminatedEventHandler(AIEntity reporter, LivingEntity eliminatedTarget);

        /// <summary>
        /// Боец запрашивает смену позиции, так как текущая неэффективна.
        /// </summary>
        [Signal]
        public delegate void RepositionRequestedEventHandler(AIEntity member);

        /// <summary>
        /// Сообщает о том, что член отряда был уничтожен.
        /// </summary>
        [Signal]
        public delegate void MemberDestroyedEventHandler(AIEntity member);
    }
}