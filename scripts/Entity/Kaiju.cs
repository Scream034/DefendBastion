using System.Threading.Tasks;
using Game.Entity.AI;
using Godot;

namespace Game.Entity;

// Изменяем наследование с LinearMoveableEntity на AIEntity
public sealed partial class Kaiju : AIEntity
{
    [Export]
    private AudioStreamPlayer3D _audio;

    public Kaiju() : base(IDs.Kaiju) { }

    public override void _Ready()
    {
        base._Ready(); // Вызываем _Ready() из AIEntity, который запустит машину состояний

        // Если хп в 0, значит Кайдзю нужно создать со случайном хп
        if (Health == 0)
        {
            SetMaxHealth(GD.RandRange(900, 2222));
        }

        GD.Print($"Kaiju created: {GlobalPosition}! Health: {Health}. AI is active.");
    }

    public override async Task<bool> DamageAsync(float amount)
    {
        if (!await base.DamageAsync(amount)) return false;

        _audio.Play();

        // Дополнительная логика: если Кайдзю получил урон, он может "разозлиться"
        // и атаковать обидчика. Для этого нужна ссылка на того, кто нанес урон.
        // Сейчас метод DamageAsync не принимает источник, но это легко расширить.

        return true;
    }
}