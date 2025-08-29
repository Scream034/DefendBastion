using Godot;
using Game.Interfaces;
using System;
using System.Threading.Tasks;
using Game.Entity;
using Game.Entity.AI;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для всех сущностей типа "турель".
/// Реализует базовую логику прочности через интерфейс IDamageable
/// и возможность взаимодействия через IInteractable.
/// </summary>
public abstract partial class BaseTurret : StaticBody3D, IDamageable, IInteractable, IFactionMember
{
    /// <summary>
    /// Событие, вызываемое при уничтожении турели.
    /// </summary>
    public event Action OnDestroyed;

    /// <summary>
    /// Событие, вызываемое при изменении здоровья (урон или починка).
    /// Передает новое значение здоровья.
    /// </summary>
    public event Action<float> OnHealthChanged;

    [ExportGroup("Faction")]
    [Export]
    public Faction Faction { get; set; } = Faction.Neutral;

    [ExportGroup("Health")]
    [Export(PropertyHint.Range, "1,10000,1")]
    public float MaxHealth { get; private set; } = 100f;

    /// <summary>
    /// Текущее количество очков прочности турели.
    /// </summary>
    public float Health { get; private set; }

    public bool IsAlive => Health > 0;

    public override void _Ready()
    {
        Health = MaxHealth;
    }

    /// <summary>
    /// Наносит урон турели.
    /// </summary>
    /// <param name="amount">Количество урона.</param>
    /// <returns>true, если урон был нанесен; false, если турель уже уничтожена.</returns>
    public virtual async Task<bool> DamageAsync(float amount, LivingEntity source = null)
    {
        if (Health <= 0) return false;

        await SetHealthAsync(Health - amount);
        return true;
    }

    /// <summary>
    /// Восстанавливает прочность турели.
    /// </summary>
    /// <param name="amount">Количество восстанавливаемой прочности.</param>
    /// <returns>true, если прочность была восстановлена; false, если турель уничтожена.</returns>
    public virtual async Task<bool> HealAsync(float amount)
    {
        // Нельзя починить уничтоженную турель
        if (Health <= 0) return false;

        await SetHealthAsync(Health + amount);
        return true;
    }

    /// <summary>
    /// Уничтожает турель.
    /// </summary>
    /// <returns>true, если команда на уничтожение была отправлена.</returns>
    public virtual Task<bool> DestroyAsync()
    {
        OnDestroyed?.Invoke();
        GD.Print($"{Name} была уничтожена!");
        QueueFree();

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public abstract void Interact(ITurretControllable entity);

    /// <inheritdoc/>
    public abstract string GetInteractionText();

    public bool IsHostile(IFactionMember other)
    {
        if (other == null) return false;
        // Prevent turrets from targeting themselves
        if (other is PhysicsBody3D otherNode && otherNode.GetRid() == GetRid()) return false;
        return FactionManager.AreFactionsHostile(Faction, other.Faction);
    }

    protected async Task SetHealthAsync(float health)
    {
        Health = health;
        if (Health <= 0)
        {
            Health = 0;
            await DestroyAsync();
        }
        else
        {
            OnHealthChanged?.Invoke(Health);
        }
    }
}