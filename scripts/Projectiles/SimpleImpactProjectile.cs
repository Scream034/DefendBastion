using Godot;
using Game.Interfaces;
using Game.VFX;

namespace Game.Projectiles;

/// <summary>
/// Базовый класс для всех снарядов в игре.
/// </summary>
public partial class SimpleImpactProjectile : BaseProjectile
{
    [Export(PropertyHint.File, "*.tscn,*.scn")]
    private PackedScene _impactVfx;

    [Export(PropertyHint.File, "*.wav,*.ogg")]
    private AudioStream _impactSfx;

    [Export]
    private AudioStreamPlayer3D _audioPlayer;

#if DEBUG
    public override void _Ready()
    {
        base._Ready();
        if (_impactVfx == null)
        {
            GD.PushError("Для снаряда не установлен VFX!");
        }

        if (_impactSfx == null)
        {
            GD.PushError("Для снаряда не установлен SFX!");
        }
        else if (_impactVfx != null && _impactVfx.InstantiateOrNull<BaseVfx3D>() == null)
        {
            GD.PushError($"Установленный VFX не является наследником базового класса VFX!");
        }
    }
#endif

    /// <summary>
    /// Логика, выполняемая при попадании снаряда в объект.
    /// </summary>
    /// <param name="body">Объект, в который попал снаряд.</param>
    public override void OnBodyEntered(Node body)
    {
        if (body is IDamageable damageable)
        {
            damageable.Damage(Damage);
        }

        var hitPosition = GlobalPosition;
        var hitNormal = -GlobalTransform.Basis.Z;

        // Создаем VFX
        if (_impactVfx != null)
        {
            var vfxInstance = _impactVfx.Instantiate<BaseVfx3D>();
            Constants.Root.AddChild(vfxInstance);
            vfxInstance.GlobalPosition = hitPosition;
            vfxInstance.LookAt(hitPosition + hitNormal, Vector3.Up);
            vfxInstance.OnFinished += vfxInstance.QueueFree;
            vfxInstance.Play();
        }

        // Прятаем снаряд
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;

        // Остановливаем таймер жизни снаряда
        lifetimeTimer.EmitSignal(SceneTreeTimer.SignalName.Timeout);
        lifetimeTimer = null;

        // Проигрываем SFX
        if (_impactSfx != null && _audioPlayer != null)
        {
            _audioPlayer.Stream = _impactSfx;
            _audioPlayer.GlobalPosition = hitPosition;
            _audioPlayer.Play();
            _audioPlayer.Finished += QueueFree;
        }
        else
        {
            QueueFree();
        }
    }
}