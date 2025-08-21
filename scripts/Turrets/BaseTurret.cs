using Godot;
using Game.Interfaces;
using System;

namespace Game.Turrets;

/// <summary>
/// Абстрактный базовый класс для всех турелей.
/// Реализует логику прочности и взаимодействия.
/// </summary>
public abstract partial class BaseTurret : Node3D, IDamageable, IInteractable
{
    [Export(PropertyHint.Range, "1,10000,1")]
    public float MaxHealth { get; protected set; } = 100f;

    public float Health { get; protected set; }

    public event Action OnDestroyed;

    public event Action<float> OnHealthChanged;

    public override void _Ready()
    {
        Health = MaxHealth;
    }

    // --- IDamageable Implementation ---

    public virtual bool Damage(float amount)
    {
        if (Health <= 0) return false;

        Health -= amount;
        if (Health <= 0)
        {
            Health = 0;
            Destroy();
        }
        else
        {
            OnHealthChanged?.Invoke(Health);
        }

        return true;
    }

    public virtual bool Heal(float amount)
    {
        if (Health <= 0) return false; // Нельзя починить уничтоженную турель

        Health = Mathf.Min(Health + amount, MaxHealth);
        OnHealthChanged?.Invoke(Health);
        return true;
    }

    public virtual bool Destroy()
    {
        if (IsQueuedForDeletion()) return true;

        OnDestroyed?.Invoke();
        GD.Print($"{Name} была уничтожена!");
        // Здесь можно добавить логику взрыва, смены модели и т.д.
        QueueFree();

        return true;
    }

    // --- IInteractable Implementation ---

    public abstract void Interact(Player.Player character);
    public abstract string GetInteractionText();
}