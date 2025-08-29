using System;
using System.Threading.Tasks;
using Game.Interfaces;
using Godot;
using Game.Entity.AI;
using Game.Entity.Components.Resources;

namespace Game.Entity;

public abstract partial class LivingEntity : CharacterBody3D, ICharacter, IFactionMember
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

    public enum IDs
    {
        Player, // Игрок
        Kaiju, // Кайдзю
        ShortShip // Маленький корабль
    }

    public IDs ID { get; protected set; }

    [Export]
    public CharacterStats Stats { get; set; }

    [ExportGroup("Faction")]
    [Export]
    public Faction Faction { get; set; } = Faction.Neutral;

    public float Health => Stats.Health;

    public float MaxHealth => Stats.MaxHealth;

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
        Stats.Initialize(this);

        if (!Stats.IsAlive)
        {
            SetProcess(false);
            SetPhysicsProcess(false);
        }
    }

    public virtual Task<bool> DamageAsync(float amount, LivingEntity source = null) => Stats.DamageAsync(amount, source);

    public virtual Task<bool> HealAsync(float amount) => Stats.HealAsync(amount);

    public virtual Task<bool> DestroyAsync() => Stats.DestroyAsync();

    public bool IsHostile(IFactionMember other)
    {
        if (other == null) return false;
        return FactionManager.AreFactionsHostile(Faction, other.Faction);
    }
}