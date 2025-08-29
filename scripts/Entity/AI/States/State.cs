namespace Game.Entity.AI.States;

/// <summary>
/// Абстрактный базовый класс для всех состояний ИИ.
/// Определяет контракт, которому должны следовать все конкретные состояния.
/// </summary>
public abstract class State(AIEntity context)
{
    protected readonly AIEntity _context = context;

    /// <summary>
    /// Вызывается при входе в это состояние. Используется для инициализации.
    /// </summary>
    public virtual void Enter() { }

    /// <summary>
    /// Вызывается при выходе из этого состояния. Используется для очистки.
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// Вызывается каждый кадр физики. Здесь находится основная логика состояния.
    /// </summary>
    public abstract void Update(float delta);
}