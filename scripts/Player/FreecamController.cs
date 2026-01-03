#nullable enable

using Godot;

namespace Game.Player;

/// <summary>
/// Контроллер для расчета движения в режиме свободной камеры (Noclip).
/// Реализует логику Minecraft-style полета (движение в плоскости XZ + вертикаль Y).
/// </summary>
public sealed class FreecamController
{
    private float _currentSpeed = 10.0f;
    private const float MinSpeed = 1.0f;
    private const float MaxSpeed = 100.0f;
    private const float SpeedStep = 2.0f;

    /// <summary>
    /// Обрабатывает ввод с мыши (колесико) для изменения базовой скорости.
    /// </summary>
    public void HandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
        {
            if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
            {
                _currentSpeed = Mathf.Clamp(_currentSpeed + SpeedStep, MinSpeed, MaxSpeed);
            }
            else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
            {
                _currentSpeed = Mathf.Clamp(_currentSpeed - SpeedStep, MinSpeed, MaxSpeed);
            }
        }
    }

    /// <summary>
    /// Вычисляет вектор смещения (Velocity * delta) для текущего кадра.
    /// </summary>
    /// <param name="headBasis">Ориентация камеры (головы).</param>
    /// <param name="delta">Время кадра.</param>
    /// <returns>Вектор смещения в глобальных координатах.</returns>
    public Vector3 CalculateMovement(Basis headBasis, float delta)
    {
        var velocity = Vector3.Zero;
        
        // Получаем множитель скорости (Boost/Slow)
        float speedMultiplier = 1.0f;
        if (Input.IsActionPressed(Constants.ActionFreecamBoost)) speedMultiplier = 2.0f;
        if (Input.IsActionPressed(Constants.ActionFreecamSlow)) speedMultiplier = 0.5f;

        float finalSpeed = _currentSpeed * speedMultiplier;

        // 1. Вертикальное движение (ось Y) - Minecraft style (Q/E)
        float verticalAxis = Input.GetAxis(Constants.ActionFreecamDown, Constants.ActionFreecamUp);
        velocity.Y = verticalAxis;

        // 2. Горизонтальное движение (плоскость XZ)
        // Получаем ввод WASD
        var inputDir = Input.GetVector(Constants.ActionMoveLeft, Constants.ActionMoveRight, Constants.ActionMoveForward, Constants.ActionMoveBackward);

        if (inputDir != Vector2.Zero)
        {
            // Получаем векторы направления камеры, проецируем их на плоскость (обнуляем Y) и нормализуем
            Vector3 forward = headBasis.Z;
            forward.Y = 0;
            forward = forward.Normalized();

            Vector3 right = headBasis.X;
            right.Y = 0;
            right = right.Normalized();

            // Складываем векторы движения
            Vector3 wishDir = (forward * inputDir.Y + right * inputDir.X).Normalized();
            
            velocity.X = wishDir.X;
            velocity.Z = wishDir.Z;
        }

        // Возвращаем итоговое смещение
        return velocity * finalSpeed * delta;
    }
    
    // Метод для вывода текущей скорости в UI (опционально)
    public float GetCurrentSpeed() => _currentSpeed;
}