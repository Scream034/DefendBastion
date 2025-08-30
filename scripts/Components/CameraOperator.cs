#nullable enable

using Game.Components.Nodes;
using Godot;

namespace Game.Components;

/// <summary>
/// Универсальный компонент-оператор для управления видом от первого/третьего лица.
/// Инкапсулирует логику обработки ввода мыши, применения ограничений вращения
/// и интеграции с Shaker3D для создания эффектов тряски.
/// Этот компонент предназначен для использования через композицию в других контроллерах (например, PlayerHead, TurretCameraController).
/// </summary>
public sealed partial class CameraOperator
{
    public float SensitivityMultiplier { get; set; } = 1.0f;

    public float MinPitch { get; set; } = -89f;
    public float MaxPitch { get; set; } = 89f;
    public float MaxYaw { get; set; } = -1f;

    // --- Внутренние ссылки и состояние ---
    private Node3D? _transformTarget;
    private Camera3D? _targetCamera;
    private Shaker3D? _shaker;

    private Vector2 _cameraRotation; // В радианах

    // Кэшированные значения в радианах для производительности
    private float _minPitchRad, _maxPitchRad, _maxYawRad;

    private Vector3 _lastPositionOffset = Vector3.Zero;

    /// <summary>
    /// Инициализирует оператор необходимыми ссылками и настройками.
    /// Должен быть вызван один раз перед первым использованием.
    /// </summary>
    /// <param name="transformTarget">Узел, к которому будет применяться вращение (например, PlayerHead или сама камера).</param>
    /// <param name="targetCamera">Узел камеры, к которому будет применяться смещение позиции от тряски.</param>
    /// <param name="shaker">Компонент Shaker3D для эффектов тряски.</param>
    public void Initialize(Node3D transformTarget, Camera3D targetCamera, Shaker3D? shaker)
    {
        if (_targetCamera != null && _shaker != null)
        {
            GD.PushWarning($"{_targetCamera.Name} Already has a shaker (Check for double initialization)!");
        }

        _transformTarget = transformTarget;
        _targetCamera = targetCamera;
        _shaker = shaker;

        // Считываем текущий поворот цели, чтобы избежать скачка при активации
        _cameraRotation = new Vector2(_transformTarget.Rotation.X, _transformTarget.Rotation.Y);

        UpdateRotationLimits();
    }

    /// <summary>
    /// Обновляет кэшированные значения ограничений в радианах.
    /// Вызывается при инициализации и при смене лимитов.
    /// </summary>
    public void UpdateRotationLimits()
    {
        _minPitchRad = Mathf.DegToRad(MinPitch);
        _maxPitchRad = Mathf.DegToRad(MaxPitch);
        _maxYawRad = Mathf.DegToRad(MaxYaw);
    }

    /// <summary>
    /// Основной метод обновления. Вызывается каждый кадр из родительского контроллера.
    /// </summary>
    /// <param name="mouseDelta">Относительное смещение мыши за кадр.</param>
    /// <param name="delta">Время, прошедшее с предыдущего кадра.</param>
    public void Update(Vector2 mouseDelta, float delta)
    {
        // Сначала обновляем состояние шейкера.
        _shaker?.Update(delta);

        // Затем обновляем поворот камеры от ввода.
        UpdateCameraRotation(mouseDelta);

        // Наконец, применяем все трансформации вместе.
        ApplyFinalTransformations();
    }

    /// <summary>
    /// Программно устанавливает поворот камеры.
    /// </summary>
    /// <param name="eulerAngles">Целевые углы Эйлера.</param>
    public void SetRotation(Vector3 eulerAngles)
    {
        _cameraRotation.X = eulerAngles.X;
        _cameraRotation.Y = eulerAngles.Y;
        ApplyCameraRotationLimits();
        ApplyFinalTransformations(); // Применяем сразу, чтобы избежать задержки в один кадр
    }

    /// <summary>
    /// Добавляет смещение к текущему повороту камеры (используется для отдачи).
    /// </summary>
    public void AddRotation(Vector3 rotationDelta)
    {
        _cameraRotation.X += rotationDelta.X;
        _cameraRotation.Y += rotationDelta.Y;
    }

    /// <summary>
    /// Обновляет поворот на основе ввода мыши.
    /// </summary>
    private void UpdateCameraRotation(Vector2 mouseDelta)
    {
        const float baseMouseSensitivity = 0.002f;

        _cameraRotation.Y -= mouseDelta.X * SensitivityMultiplier * baseMouseSensitivity;
        _cameraRotation.X -= mouseDelta.Y * SensitivityMultiplier * baseMouseSensitivity;

        ApplyCameraRotationLimits();
    }

    /// <summary>
    /// Применяет ограничения к углам поворота камеры.
    /// </summary>
    private void ApplyCameraRotationLimits()
    {
        _cameraRotation.X = Mathf.Clamp(_cameraRotation.X, _minPitchRad, _maxPitchRad);
        if (_maxYawRad >= 0)
        {
            _cameraRotation.Y = Mathf.Clamp(_cameraRotation.Y, -_maxYawRad, _maxYawRad);
        }
    }

    /// <summary>
    /// Применяет итоговые трансформации к целевым узлам.
    /// </summary>
    private void ApplyFinalTransformations()
    {
        var finalRotation = CalculateFinalRotation();
        var finalPosition = CalculateFinalPosition();

        if (_transformTarget != null)
        {
            _transformTarget.Rotation = new Vector3(finalRotation.X, finalRotation.Y, _transformTarget.Rotation.Z);
        }

        if (_targetCamera != null)
        {
            _targetCamera.Position = finalPosition;
        }
    }

    /// <summary>
    /// Вычисляет финальный поворот с учетом тряски.
    /// </summary>
    private Vector3 CalculateFinalRotation()
    {
        var baseRotation = new Vector3(_cameraRotation.X, _cameraRotation.Y, 0);

        if (_shaker != null)
        {
            var (_, rotOffset) = _shaker.GetCurrentShakeOffsets();
            return baseRotation + rotOffset;
        }

        return baseRotation;
    }

    /// <summary>
    /// Вычисляет финальную позицию с учетом тряски.
    /// </summary>
    private Vector3 CalculateFinalPosition()
    {
        if (!GodotObject.IsInstanceValid(_shaker) || !GodotObject.IsInstanceValid(_targetCamera))
        {
            _shaker = null;
            _targetCamera = null;
            return _targetCamera?.Position ?? Vector3.Zero;
        }

        var (currentPositionOffset, _) = _shaker.GetCurrentShakeOffsets();

        // Берем текущую позицию камеры (которую мог установить RemoteTransform3D).
        // Отнимаем смещение тряски с *предыдущего* кадра, чтобы "очистить" ее.
        // Добавляем смещение тряски для *текущего* кадра.
        Vector3 basePosition = _targetCamera.Position - _lastPositionOffset;
        Vector3 newPosition = basePosition + currentPositionOffset;

        // Запоминаем текущее смещение, чтобы отменить его на следующем кадре.
        _lastPositionOffset = currentPositionOffset;
        return newPosition;
    }
}
#nullable disable