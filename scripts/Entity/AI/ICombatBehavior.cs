namespace Game.Entity.AI.Behaviors
{
    /// <summary>
    /// Интерфейс для стратегий боевого поведения. Определяет, как ИИ двигается
    /// и когда атакует в состоянии боя.
    /// </summary>
    public interface ICombatBehavior
    {
        /// <summary>
        /// Вызывается каждый кадр из AttackState. Содержит всю логику боя.
        /// </summary>
        void Process(AIEntity context, double delta);
    }
}