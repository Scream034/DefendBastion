using Godot;

namespace Game.Entity;

public sealed partial class Kaiju : LinearMoveableEntity
{
    [Export]
    private AudioStream _impactSound1;

    [Export]
    private AudioStream _impactSound2;

    [Export]
    private AudioStreamPlayer3D _audio;

    public Kaiju() : base(IDs.Kaiju) { }

    public override void _Ready()
    {
        base._Ready();

        // Если хп в 0, значит Кайдзю нужно создать со случайном хп
        if (Health == 0)
        {
            SetMaxHealth(GD.RandRange(900, 2222));
        }

        GD.Print("Kaiju created! Health: " + Health);
    }

    public override bool Damage(float amount)
    {
        if (!base.Damage(amount)) return false;

        _audio.Play();

        return true;
    }
}