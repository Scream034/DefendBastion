using Godot;
using Game.VFX;
using System.Threading.Tasks;

namespace Game.Projectiles;

/// <summary>
/// Снаряд, который при попадании создает визуальный и звуковой эффект.
/// Использует гибридный подход для определения точной точки и нормали попадания.
/// </summary>
public partial class SimpleImpactProjectile : BaseProjectile
{
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    private PackedScene _impactVfx;

    [Export(PropertyHint.File, "*.wav,*.ogg")]
    private AudioStream _impactSfx;

    [Export]
    private AudioStreamPlayer3D _audioPlayer;

    private bool HasSFX => _impactSfx != null && _audioPlayer != null;

    public override void _Ready()
    {
        base._Ready();
        if (HasSFX)
        {
            _audioPlayer.Stream = _impactSfx;
            _audioPlayer.Finished += () =>
            {
#if DEBUG
                GD.Print($"[{this}] Будет возвращен в пул из-за окончания звука!");
#endif
                ProjectilePool.Return(this);
            };
        }

#if DEBUG
        if (_impactVfx == null)
        {
            GD.PushError($"Для снаряда '{Name}' не установлен VFX попадания!");
        }

        if (_impactSfx == null)
        {
            GD.PushWarning($"Для снаряда '{Name}' не установлен SFX попадания.");
        }
        else if (_impactVfx != null && _impactVfx.InstantiateOrNull<BaseVfx3D>() == null)
        {
            // Эта проверка может быть полезной, если у вас есть базовый класс для VFX
            GD.PushError($"Установленный VFX для снаряда '{Name}' не является наследником BaseVfx3D!");
        }
#endif
    }

    /// <summary>
    /// Логика, выполняемая при попадании снаряда в объект.
    /// </summary>
    protected override async Task OnHit(Godot.Collections.Dictionary hitInfo)
    {
        // Вызываем базовую логику, которая наносит урон и перемещает снаряд.
        //    Этот метод больше НЕ возвращает снаряд в пул.
        await HandleHitAndDamage(hitInfo);

        var hitPosition = hitInfo["position"].AsVector3();
        var hitNormal = hitInfo["normal"].AsVector3();

        // Создаем VFX, как и раньше.
        if (_impactVfx != null)
        {
            var vfxInstance = _impactVfx.Instantiate<Node3D>();
            Constants.Root.AddChild(vfxInstance);
            vfxInstance.GlobalPosition = hitPosition;
            vfxInstance.LookAt(hitPosition + hitNormal, Vector3.Up);

            if (vfxInstance is GpuParticles3D particles)
            {
                particles.Emitting = true;
                Constants.Tree.CreateTimer(particles.Lifetime).Timeout += vfxInstance.QueueFree;
            }
            else if (vfxInstance is BaseVfx3D baseVfx)
            {
                baseVfx.OnFinished += baseVfx.QueueFree;
                baseVfx.Play();
            }
        }

        // Прячем снаряд и отключаем его физику, чтобы он не летел дальше,
        //    пока проигрывается звук.
        HideAndDisable();

        // Реализуем логику возврата в пул.
        if (HasSFX)
        {
            // Если есть звук, проигрываем его. Снаряд вернется в пул
            // по сигналу Finished, который мы подцепили в _Ready().
            _audioPlayer.Play();
        }
        else
        {
            // Если звука нет, возвращаем снаряд в пул немедленно.
            ProjectilePool.Return(this);
        }
    }


    /// <summary>
    /// Вспомогательный метод, чтобы спрятать и отключить физику снаряда,
    /// позволяя ему "дожить" до окончания проигрывания звука.
    /// </summary>
    private void HideAndDisable()
    {
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);

        // Отключаем дальнейшее обнаружение столкновений
        collisionShape.Disabled = true;

        // Останавливаем таймер жизни, так как снаряд уже "умер"
        lifetimeTimer.Stop();
    }
}