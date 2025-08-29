using Game.Entity;
using Game.Interfaces;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для всех сущностей типа "турель".
/// </summary>
public abstract partial class BaseTurret : LivingEntity, IInteractable
{
    /// <inheritdoc/>
    public abstract void Interact(ITurretControllable entity);

    /// <inheritdoc/>
    public abstract string GetInteractionText();
}