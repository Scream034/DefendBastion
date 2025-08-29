using System;
using System.Threading.Tasks;
using Game.Interfaces;
using Godot;
using Game.Entity.AI;

namespace Game.Entity;

public abstract partial class LivingEntity : CharacterBody3D, ICharacter, IFactionMember
{
    public event Action OnDestroyed;
    public event Action<float> OnHealthChanged;

    public enum IDs
    {
        Player, // Игрок
        Kaiju, // Кайдзю
        ShortShip // Маленький корабль
    }

    public IDs ID { get; protected set; }

    [ExportGroup("Faction")]
    [Export]
    public Faction Faction { get; set; } = Faction.Neutral;

    [ExportGroup("Health & Durability")]
    [Export(PropertyHint.Range, "0,30000")]
    public float MaxHealth { get; private set; } = 100f;
    public float Health { get; private set; }
    public bool IsAlive => Health > 0;

    [ExportGroup("Combat Modifiers")]
    [Export(PropertyHint.Range, "0.1, 10.0, 0.1")]
    public float DamageMultiplier { get; private set; } = 1.0f;

    [Export(PropertyHint.Range, "0.0, 1.0, 0.01")]
    public float DamageResistance { get; private set; } = 0.0f;

    [Export(PropertyHint.Range, "0, 1000, 1")]
    public float Armor { get; private set; } = 0.0f;

    [ExportGroup("AI Threat Evaluation")]
    [Export(PropertyHint.Range, "0, 1000, 10")]
    public float BaseThreatValue { get; private set; } = 100f;

    /// <summary>
    /// Исполузуется для Godot-компилятора
    /// </summary>
    public LivingEntity() { }

    protected LivingEntity(IDs id)
    {
        ID = id;
    }

    public override void _EnterTree()
    {
        LivingEntityManager.Add(this);
    }

    public override void _ExitTree()
    {
        LivingEntityManager.Remove(this);
    }

    public override void _Ready()
    {
        Health = MaxHealth;

        if (!IsAlive)
        {
            SetProcess(false);
            SetPhysicsProcess(false);
        }
    }

    public float CalculateIncomingDamage(float baseDamage)
    {
        float finalDamage = baseDamage * (1.0f - DamageResistance);
        finalDamage -= Armor;
        return finalDamage < 0 ? 0 : finalDamage;
    }

    public virtual async Task<bool> DamageAsync(float amount, LivingEntity source = null)
    {
        if (!IsAlive) return false;

        float finalDamage = CalculateIncomingDamage(amount);
        await SetHealthAsync(Health - finalDamage);
        GD.Print($"{Name} took {finalDamage} damage from {source?.Name}!");
        return true;
    }

    public virtual async Task<bool> HealAsync(float amount)
    {
        if (!IsAlive || Health >= MaxHealth) return false;
        await SetHealthAsync(Health + amount);
        GD.Print($"{Name} healed for {amount}!");
        return true;
    }

    public virtual Task<bool> DestroyAsync()
    {
        if (IsQueuedForDeletion()) return Task.FromResult(false);

        GD.Print($"Entity {Name} died!");
        QueueFree();
        OnDestroyed?.Invoke();

        return Task.FromResult(true);
    }

    protected async Task SetMaxHealthAsync(float health)
    {
        MaxHealth = Mathf.Clamp(health, 0, MaxHealth);
        await SetHealthAsync(Health);
    }

    protected async Task SetHealthAsync(float health)
    {
        Health = Mathf.Clamp(health, 0, MaxHealth);
        OnHealthChanged?.Invoke(Health);

        if (Health <= 0)
        {
            await DestroyAsync();
        }
    }

    public bool IsHostile(IFactionMember other)
    {
        if (other == null) return false;
        return FactionManager.AreFactionsHostile(Faction, other.Faction);
    }
}