using Godot;
using Game.Interfaces;
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

#if DEBUG
    public override void _Ready()
    {
        base._Ready();
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
    public override void OnBodyEntered(Node body)
    {
        if (IgnoredEntities.Contains(body))
        {
            return;
        }

        if (body is IDamageable damageable)
        {
            damageable.Damage(Damage);
        }

        // --- НОВАЯ УЛУЧШЕННАЯ ЛОГИКА ОПРЕДЕЛЕНИЯ НОРМАЛИ ---

        // Значения по умолчанию на случай, если RayCast не сработает
        Vector3 hitPosition = GlobalPosition;
        Vector3 hitNormal = -GlobalTransform.Basis.Z; // Направление, обратное полету пули

        // 1. Получаем прямое состояние физического пространства для выполнения запросов.
        var spaceState = GetWorld3D().DirectSpaceState;
        if (spaceState != null)
        {
            // 2. Создаем параметры для луча. Мы стреляем очень коротким лучом из текущей позиции
            //    немного назад, чтобы гарантированно пересечь поверхность, с которой столкнулись.
            var query = PhysicsRayQueryParameters3D.Create(
                GlobalPosition, // Начало луча (центр снаряда)
                GlobalPosition - GlobalTransform.Basis.Z * 0.5f, // Конец луча (чуть вперед по направлению полета)
                CollisionMask, // Используем ту же маску столкновений, что и у Area3D
                [GetRid()] // Исключаем из запроса сам снаряд
            );

            // 3. Выполняем запрос
            var result = spaceState.IntersectRay(query);

            // 4. Если луч что-то нашел, используем точные данные из результата.
            if (result.Count > 0)
            {
                hitPosition = (Vector3)result["position"];
                hitNormal = (Vector3)result["normal"];
            }
        }
        // Если RayCast ничего не нашел (крайне редкий случай), будут использованы значения по умолчанию.
        // Это делает код более надежным.

        // --- КОНЕЦ НОВОЙ ЛОГИКИ ---

        // Создаем VFX с использованием точной позиции и нормали
        if (_impactVfx != null)
        {
            var vfxInstance = _impactVfx.Instantiate<Node3D>(); // Безопаснее инстанциировать как Node3D
            GetTree().Root.AddChild(vfxInstance);
            vfxInstance.GlobalPosition = hitPosition;

            // LookAt правильно сориентирует VFX перпендикулярно поверхности
            vfxInstance.LookAt(hitPosition + hitNormal, Vector3.Up);

            // Если ваш VFX - это частицы, которые нужно запустить
            if (vfxInstance is GpuParticles3D particles)
            {
                particles.Emitting = true;
                // Можно добавить таймер на удаление, если у частиц нет самоуничтожения
                GetTree().CreateTimer(particles.Lifetime).Timeout += vfxInstance.QueueFree;
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

        // Проигрываем SFX и удаляем снаряд после окончания звука
        if (_impactSfx != null && _audioPlayer != null)
        {
            // Перемещаем плеер из снаряда в сцену, чтобы он не удалился вместе со снарядом
            RemoveChild(_audioPlayer);
            GetTree().Root.AddChild(_audioPlayer);
            
            _audioPlayer.Stream = _impactSfx;
            _audioPlayer.GlobalPosition = hitPosition;
            _audioPlayer.Play();
            _audioPlayer.Finished += () =>
            {
                _audioPlayer.QueueFree(); // Удаляем плеер после проигрывания
                QueueFree(); // Окончательно удаляем узел снаряда
            };
        }
        else
        {
            // Если звука нет, удаляем снаряд сразу
            QueueFree();
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
        if (GetChild(0) is CollisionShape3D collisionShape)
        {
            collisionShape.Disabled = true;
        }

        // Останавливаем таймер жизни, так как снаряд уже "умер"
        lifetimeTimer?.EmitSignal(SceneTreeTimer.SignalName.Timeout);
        lifetimeTimer = null;
    }
}