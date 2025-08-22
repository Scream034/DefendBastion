using Game.Entity;
using Godot;

namespace Game;

public partial class GameManager : Node3D
{
    [Export]
    private AudioStreamPlayer _audioAmbient;

    [Export]
    private AudioStreamPlayer _audioVictory;

    [Export]
    private AudioStreamPlayer _audioDefeat;

    private Kaiju _kaiju;

    public GameManager()
    {
        LivingEntityManager.OnAdded += OnEntityAdded;
        LivingEntityManager.OnRemoved += OnEntityRemoved;
    }

    public override void _Ready()
    {
        base._Ready();
        KaijuHealthChanged(_kaiju.Health);
#if DEBUG
        AudioServer.SetBusVolumeDb(0, -16f);
#endif
    }

    private void OnEntityAdded(LivingEntity sender)
    {
        if (sender is Kaiju kaiju)
        {
            _kaiju = kaiju;
            _kaiju.OnDestroyed += OnKaijuDeath;
            _kaiju.OnHealthChanged += KaijuHealthChanged;
        }
    }

    private void OnEntityRemoved(LivingEntity sender)
    {
        if (sender is Kaiju)
        {
            _kaiju = null;
        }
    }

    private void OnKaijuDeath()
    {
        GD.Print($"Victory! Kaiju is dead!");

        _audioAmbient.Stop();
        _audioVictory.Play();

        UI.Instance.ShowEndState("ПОБЕДА! Кайдзю был уничтожен!");
    }

    private void OnTargetZoneEnter(Node3D body)
    {
        if (body is not Kaiju) return;
        GD.Print("Lose! Bastion fell!");

        DetachEventsKaiju();

        _audioAmbient.Stop();
        _audioDefeat.Play();

        UI.Instance.ShowEndState("ПОРАЖЕНИЕ! Бастион пал!");
    }

    private void KaijuHealthChanged(float health)
    {
        UI.Instance.UpdateProgress(health / _kaiju.MaxHealth * 100f);
    }

    public void DetachEventsKaiju()
    {
        _kaiju.OnDestroyed -= OnKaijuDeath;
        _kaiju.OnHealthChanged -= KaijuHealthChanged;
    }
}