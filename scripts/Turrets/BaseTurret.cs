using Game.Entity;
using Game.Entity.AI;
using Game.Entity.Components.Resources;
using Game.Interfaces;
using Godot;
using System;
using System.Threading.Tasks;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для всех сущностей типа "турель".
/// </summary>
public abstract partial class BaseTurret : StaticBody3D, ICharacter, IInteractable, IFactionMember
{
    public virtual event Action OnDestroyed
    {
        add => Stats.OnDestroyed += value;
        remove => Stats.OnDestroyed -= value;
    }

    public virtual event Action<float> OnHealthChanged
    {
        add => Stats.OnHealthChanged += value;
        remove => Stats.OnHealthChanged -= value;
    }

    [Export]
    public CharacterStats Stats { get; set; }

    [ExportGroup("Faction")]
    [Export]
    public Faction Faction { get; set; } = Faction.Player;

    public float Health => Stats.Health;

    public float MaxHealth => Stats.MaxHealth;

    public override void _Ready()
    {
        base._Ready();
        Stats.Initialize(this);
    }

    public virtual Task<bool> DamageAsync(float amount, LivingEntity source = null) => Stats.DamageAsync(amount, source);

    public virtual Task<bool> HealAsync(float amount) => Stats.HealAsync(amount);

    public virtual Task<bool> DestroyAsync() => Stats.DestroyAsync();

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
}