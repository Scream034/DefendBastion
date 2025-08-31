namespace Game.Entity.AI.Behaviors
{
    /// <summary>
    /// Интерфейс для стратегий боевого поведения. Определяет, как ИИ двигается
    /// и когда атакует в состоянии боя.
    /// </summary>
    public interface ICombatBehavior
    {
        public float AttackRange { get; }
        public float AttackCooldown { get; }
        
        /// <summary>
        /// Конкретное действие атаки, которое выполняет это боевое поведение.
        /// </summary>
        public IAttackAction Action { get; } // <--- ДОБАВЛЕНО ЭТО СВОЙСТВО

        /// <summary>
        /// Вызывается при входе в состояние атаки. Подготавливает поведение к бою.
        /// </summary>
        void EnterCombat(AIEntity context);

        /// <summary>
        /// Вызывается при выходе из состояния атаки. Очищает состояние поведения.
        /// </summary>
        void ExitCombat(AIEntity context);
    }
}