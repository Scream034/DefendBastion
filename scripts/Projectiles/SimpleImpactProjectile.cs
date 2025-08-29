using Godot;
using Game.VFX;
using System.Threading.Tasks;
using Game.Singletons;

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

    // Константа для сравнения с плавающей точкой во избежание магических чисел
    private const float ALMOST_ONE = 0.999f;

    public override void _Ready()
    {
        base._Ready();
        if (HasSFX)
        {
            _audioPlayer.Stream = _impactSfx;
            _audioPlayer.Finished += () =>
            {
#if DEBUG
                GD.Print($"{Name} Will return to pool because audio is finished!");
#endif
                ProjectilePool.Return(this);
            };
        }

#if DEBUG
        if (_impactVfx == null)
        {
            GD.PushError($"For projectile '{Name}' no VFX is set!");
        }

        if (_impactSfx == null)
        {
            GD.PushWarning($"For projectile '{Name}' no SFX is set.");
        }
        else if (_impactVfx != null && _impactVfx.InstantiateOrNull<BaseVfx3D>() == null)
        {
            GD.PushError($"Current projectile VFX is not BaseVfx3D!");
        }
#endif
    }

    /// <summary>
    /// Логика, выполняемая при попадании снаряда в объект.
    /// </summary>
    protected override async Task OnHit(Godot.Collections.Dictionary hitInfo)
    {
        // Вызываем базовую логику, которая наносит урон и перемещает снаряд.
        await HandleHitAndDamage(hitInfo);

        var hitPosition = hitInfo["position"].AsVector3();
        var hitNormal = hitInfo["normal"].AsVector3();

        if (_impactVfx != null)
        {
            var vfxInstance = _impactVfx.Instantiate<Node3D>();
            Constants.Root.AddChild(vfxInstance);
            vfxInstance.GlobalPosition = hitPosition;

            // --- НАЧАЛО ИСПРАВЛЕНИЯ ---
            // Проверяем, не является ли нормаль попадания почти вертикальной.
            // Используем скалярное произведение, чтобы определить коллинеарность с вектором Vector3.Up.
            // Mathf.Abs(hitNormal.Dot(Vector3.Up)) будет близко к 1, если векторы параллельны.
            Vector3 upVector;
            if (Mathf.Abs(hitNormal.Dot(Vector3.Up)) > ALMOST_ONE)
            {
                // Если мы попали в пол или потолок, используем Vector3.Forward как "верх",
                // чтобы дать LookAt однозначное направление для ориентации.
                upVector = Vector3.Forward;
            }
            else
            {
                // В остальных случаях стандартный Vector3.Up подходит.
                upVector = Vector3.Up;
            }

            vfxInstance.LookAt(hitPosition + hitNormal, upVector);
            // --- КОНЕЦ ИСПРАВЛЕНИЯ ---

            if (vfxInstance is BaseVfx3D baseVfx)
            {
                baseVfx.OnFinished += baseVfx.QueueFree;
                baseVfx.Play();
            }
        }

        // Прячем снаряд и отключаем его физику, чтобы он не летел дальше,
        // пока проигрывается звук.
        HideAndDisable();

        if (HasSFX)
        {
            // Если есть звук, проигрываем его. Снаряд вернется в пул по сигналу Finished.
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

        if (collisionShape != null)
        {
            collisionShape.Disabled = true;
        }

        lifetimeTimer.Stop();
    }
}