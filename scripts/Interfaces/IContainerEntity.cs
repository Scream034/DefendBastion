using Game.Entity;

namespace Game.Interfaces;

/// <summary>
/// Представляет сущность, которая может содержать или управляться другой сущностью (например, турель с пилотом, транспорт с пассажирами).
/// </summary>
public interface IContainerEntity
{
    /// <summary>
    /// Возвращает основную сущность, управляющую этим контейнером или находящуюся в нём.
    /// Например, пилота турели. Может вернуть null, если контейнер пуст или автономен.
    /// </summary>
    /// <returns>Управляющая сущность в виде PhysicsBody3D, которую можно атаковать.</returns>
    LivingEntity GetContainedEntity();
}