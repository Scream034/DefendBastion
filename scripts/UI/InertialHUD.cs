#nullable enable
using Godot;
using Game.Player;

namespace Game.UI;

/// <summary>
/// Контроллер корневого узла UI.
/// Создает эффект инерции (запаздывания) интерфейса при движении и поворотах.
/// </summary>
public partial class InertialHUD : Control
{
    [ExportGroup("Physics Factors")]
    [Export(PropertyHint.Range, "0, 2")] public float VelocityInfluence { get; set; } = 0.7f; // Влияние стрейфов
    [Export(PropertyHint.Range, "0, 1")] public float MouseLookInfluence { get; set; } = 0.12f; // Влияние поворота головы

    [ExportGroup("Spring Settings")]
    [Export] public float Smoothness { get; set; } = 0.15f;  // Плавность (чем меньше, тем больше "мыла")
    [Export] public float ReturnSpeed { get; set; } = 15.0f; // Скорость возврата в центр
    [Export] public float MaxOffset { get; set; } = 20.0f;  // Лимит смещения в пикселях

    private Vector2 _targetOffset = Vector2.Zero;
    private Vector2 _currentOffset = Vector2.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            _mouseDelta = motion.Relative;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 1. Расчет влияния МЫШИ (поворот головы)
        // Если мышь влево -> интерфейс улетает вправо (отстает)
        Vector2 lookOffset = -_mouseDelta * MouseLookInfluence;

        // 2. Расчет влияния СКОРОСТИ (тела)
        Vector2 moveOffset = Vector2.Zero;
        if (LocalPlayer.Instance != null)
        {
            // Получаем локальную скорость игрока (относительно направления взгляда)
            // Это важно: если мы идем вперед, X не меняется. Если стрейфим - меняется X.
            // Нам нужен именно боковой стрейф и вертикальное движение (прыжок).

            // Transform.Basis.Inverse() * Velocity переводит глобальную скорость в локальную
            Vector3 localVel = LocalPlayer.Instance.Head.GlobalBasis.Inverse() * LocalPlayer.Instance.Velocity;

            // localVel.X - это стрейф (Left/Right)
            // localVel.Y - это прыжок (Up/Down)
            // localVel.Z - это вперед/назад (обычно не смещает UI по осям X/Y, можно сделать Scale)

            // Инвертируем: стрейф вправо -> интерфейс влево
            moveOffset.X = -localVel.X * VelocityInfluence * 2.0f; // Усиливаем эффект стрейфа
            moveOffset.Y = localVel.Y * VelocityInfluence;         // Прыжок тянет интерфейс вниз/вверх
        }

        // Суммируем импульсы
        _targetOffset += lookOffset + moveOffset;

        // Ограничиваем (Clamp), чтобы интерфейс не улетел за экран
        _targetOffset.X = Mathf.Clamp(_targetOffset.X, -MaxOffset, MaxOffset);
        _targetOffset.Y = Mathf.Clamp(_targetOffset.Y, -MaxOffset, MaxOffset);

        // 3. Физика пружины (Spring physics)
        // Lerp к цели (input lag)
        _currentOffset = _currentOffset.Lerp(_targetOffset, Smoothness);

        // Lerp цели к нулю (возврат пружины)
        _targetOffset = _targetOffset.Lerp(Vector2.Zero, dt * ReturnSpeed);

        // Применяем
        Position = _currentOffset;

        // Сбрасываем дельту мыши, так как она накапливается в _Input
        _mouseDelta = Vector2.Zero;
    }
}