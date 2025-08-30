using System.Threading.Tasks;
using Game.Entity.AI;
using Godot;

namespace Game.Entity;

public sealed partial class Pawn : AIEntity
{
    [Export]
    private AudioStreamPlayer3D _audio;

    public override async Task<bool> DamageAsync(float amount, LivingEntity source = null)
    {
        if (!await base.DamageAsync(amount, source)) return false;

        _audio.Play();

        // Дополнительная логика: если Кайдзю получил урон, он может "разозлиться"
        // и атаковать обидчика. Для этого нужна ссылка на того, кто нанес урон.
        // Сейчас метод DamageAsync не принимает источник, но это легко расширить.

        return true;
    }
}