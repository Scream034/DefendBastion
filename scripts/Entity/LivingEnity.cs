using System;
using System.Threading.Tasks;
using Game.Interfaces;
using Godot;
using Game.Entity.AI;

namespace Game.Entity;

public abstract partial class LivingEntity : CharacterBody3D, IDamageable, IFactionMember
{
    public enum IDs
    {
        Player, // Игрок
        Kaiju, // Кайдзю
        ShortShip // Маленький корабль
    }

    public IDs ID { get; protected set; }

    [Export(PropertyHint.Range, "0,30000")]
    public float MaxHealth { get; private set; }

    public float Health { get; private set; } = 100;

    public bool IsAlive => Health > 0;
    
    [ExportGroup("Faction")]
    [Export]
    public Faction Faction { get; set; } = Faction.Neutral;

    public event Action OnDestroyed;

    public event Action<float> OnHealthChanged;

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
        base._EnterTree();
        // Это самое надежное место для регистрации, так как узел точно попадает в сцену.
        LivingEntityManager.Add(this);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        LivingEntityManager.Remove(this);
    }

    public override void _Ready()
    {
        base._Ready();
        MaxHealth = Health;

        if (Health < 0)
        {
            SetProcess(false);
            SetPhysicsProcess(false);
        }
    }

    public virtual async Task<bool> DamageAsync(float amount, LivingEntity source = null)
    {
        if (Health <= 0) return false;
        await SetHealthAsync(Health - amount);
        return true;
    }

    public virtual async Task<bool> HealAsync(float amount)
    {
        if (Health >= MaxHealth) return false;
        await SetHealthAsync(Health + amount);
        GD.Print($"Entity {ID} healed {amount} -> {Health}!");

        return true;
    }

    public virtual Task<bool> DestroyAsync()
    {
        if (IsQueuedForDeletion()) return Task.FromResult(false);

        SetProcess(false);
        SetPhysicsProcess(false);

        GD.Print($"Entity {ID} died!");
        QueueFree();
        OnDestroyed?.Invoke();

        return Task.FromResult(true);
    }
    
    public bool IsHostile(IFactionMember other)
    {
        if (other == null) return false;
        return FactionManager.AreFactionsHostile(Faction, other.Faction);
    }

    protected void SetMaxHealth(float health)
    {
        MaxHealth = health;
        Health = MaxHealth;
        OnHealthChanged?.Invoke(Health);
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