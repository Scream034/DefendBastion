using System;
using System.Threading.Tasks;
using Game.Interfaces;
using Godot;

namespace Game.Entity.Components.Resources;

/// <summary>
/// Ресурс для хранения и управления боевыми характеристиками, такими как здоровье, броня и урон.
/// Реализует логику получения урона, лечения и смерти, инкапсулируя ее от конкретных классов (DRY).
/// </summary>
[GlobalClass]
public partial class CharacterStats : Resource, IDamageable
{
    private Node3D _owner;

    public event Action OnDestroyed;
    public event Action<float> OnHealthChanged;

    [ExportGroup("Health & Durability")]
    [Export(PropertyHint.Range, "0,30000")]
    public float MaxHealth { get; set; } = 100f;
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
    /// Инициализирует компонент, устанавливая начальное здоровье и владельца.
    /// </summary>
    public void Initialize(Node3D owner)
    {
        _owner = owner;
        Health = MaxHealth;
    }

    /// <summary>
    /// Рассчитывает финальный урон с учетом сопротивления и брони.
    /// </summary>
    public float CalculateIncomingDamage(float baseDamage)
    {
        float finalDamage = baseDamage * (1.0f - DamageResistance);
        finalDamage -= Armor;
        return finalDamage < 0 ? 0 : finalDamage;
    }

    public async Task<bool> DamageAsync(float amount, LivingEntity source = null)
    {
        if (!IsAlive) return false;

        float finalDamage = CalculateIncomingDamage(amount);
        await SetHealthAsync(Health - finalDamage);
        return true;
    }

    public async Task<bool> HealAsync(float amount)
    {
        if (!IsAlive || Health >= MaxHealth) return false;
        await SetHealthAsync(Health + amount);
        return true;
    }

    public Task<bool> DestroyAsync()
    {
        if (_owner.IsQueuedForDeletion()) return Task.FromResult(false);

        _owner.SetProcess(false);
        _owner.SetPhysicsProcess(false);

        GD.Print($"Entity {_owner.Name} died!");
        _owner.QueueFree();
        OnDestroyed?.Invoke();

        return Task.FromResult(true);
    }

    private async Task SetHealthAsync(float health)
    {
        Health = Mathf.Clamp(health, 0, MaxHealth);
        OnHealthChanged?.Invoke(Health);

        if (Health <= 0)
        {
            await DestroyAsync();
        }
    }
}