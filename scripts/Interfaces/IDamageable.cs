using System;

namespace Game.Interfaces;

/// <summary>
/// Интерфейс для всех объектов, которые могут получать урон.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Текущее количество очков прочности.
    /// </summary>
    float Health { get; }

    /// <summary>
    /// Максимальное количество очков прочности.
    /// </summary>
    float MaxHealth { get; }

    /// <summary>
    /// Наносит урон объекту.
    /// </summary>
    /// <param name="amount">Количество урона.</param>
    bool Damage(float amount);

    /// <summary>
    /// Ремонтирует объект.
    /// </summary>
    /// <param name="amount">Количество восстанавливаемой прочности.</param>
    bool Heal(float amount);

    /// <summary>
    /// Уничтожает объект.
    /// </summary>
    bool Destroy();

    /// <summary>
    /// Событие, вызываемое при уничтожении объекта.
    /// </summary>
    event Action OnDestroyed;

    /// <summary>
    /// Событие, вызываемое при изменении здоровья объекта.
    /// </summary>
    event Action<float> OnHealthChanged;
}