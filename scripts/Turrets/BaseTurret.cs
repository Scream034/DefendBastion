using Game.Entity;
using Game.Interfaces;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для всех сущностей типа "турель".
/// </summary>
public partial class BaseTurret : LivingEntity, IInteractable
{
    /// <inheritdoc/>
    public virtual void Interact(ITurretControllable entity) { }

    /// <inheritdoc/>
    public virtual string GetInteractionText() { return string.Empty; }
}