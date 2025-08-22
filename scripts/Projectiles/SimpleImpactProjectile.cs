using Godot;
using Game.VFX;

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
            _audioPlayer.Finished += () => ProjectilePool.Return(this);
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
    }
#endif

    /// <summary>
    /// Логика, выполняемая при попадании снаряда в объект.
    /// </summary>
    /// <param name="body">Объект, в который попал снаряд.</param>
    protected override void OnHit(Godot.Collections.Dictionary hitInfo)
    {
        base.OnHit(hitInfo);

        var hitPosition = hitInfo["position"].AsVector3();
        var hitNormal = hitInfo["normal"].AsVector3();

        // Создаем VFX с использованием точной позиции и нормали
        if (_impactVfx != null)
        {
            var vfxInstance = _impactVfx.Instantiate<Node3D>(); // Безопаснее инстанциировать как Node3D
            Constants.Root.AddChild(vfxInstance);
            vfxInstance.GlobalPosition = hitPosition;

            // LookAt правильно сориентирует VFX перпендикулярно поверхности
            vfxInstance.LookAt(hitPosition + hitNormal, Vector3.Up);

            // Если ваш VFX - это частицы, которые нужно запустить
            if (vfxInstance is GpuParticles3D particles)
            {
                particles.Emitting = true;
                // Можно добавить таймер на удаление, если у частиц нет самоуничтожения
                Constants.Tree.CreateTimer(particles.Lifetime).Timeout += vfxInstance.QueueFree;
            }
            // Если у вас кастомный класс VFX с методами Play/OnFinished
            else if (vfxInstance is BaseVfx3D baseVfx)
            {
                baseVfx.OnFinished += baseVfx.QueueFree;
                baseVfx.Play();
            }
        }

        // Прячем снаряд, чтобы он не летел дальше, пока проигрывается звук
        HideAndDisable();

        // Мы больше не отсоединяем плеер и не заставляем его самоуничтожаться.
        // Он просто проигрывает звук и будет переиспользован вместе со снарядом.
        if (HasSFX)
        {
            _audioPlayer.Play();
        }
        else
        {
            // Если звука нет, удаляем снаряд сразу
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
        lifetimeTimer?.EmitSignal(SceneTreeTimer.SignalName.Timeout);
        lifetimeTimer = null;
    }
}