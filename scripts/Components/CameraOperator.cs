#nullable enable

using Game.Components.Nodes;
using Game.Singletons;
using Godot;

namespace Game.Components;

/// <summary>
/// Универсальный компонент-оператор для управления видом.
/// Инкапсулирует логику вращения, ограничений и тряски.
/// </summary>
public sealed partial class CameraOperator
{
    // Базовое значение: сколько радиан на 1 пиксель движения мыши (RAW input).
    // Это "Hardware" константа.
    private const float BaseRawSensitivity = 0.0011f;

    /// <summary>
    /// Индивидуальная чувствительность узла (например, у тяжелой турели = 0.5, у игрока = 1.0).
    /// </summary>
    public float NodeSensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Динамический множитель (зум, дебаффы). По умолчанию 1.0.
    /// </summary>
    public float DynamicSensitivity { get; set; } = 1.0f;

    public float MinPitch { get; set; } = -80f;
    public float MaxPitch { get; set; } = 80f;
    public float MaxYaw { get; set; } = -1f;

    private Node3D? _transformTarget;
    private Camera3D? _targetCamera;
    private Shaker3D? _shaker;

    private Vector2 _cameraRotation;
    private float _minPitchRad, _maxPitchRad, _maxYawRad;
    private Vector3 _lastPositionOffset = Vector3.Zero;

    /// <summary>
    /// Инициализирует оператор.
    /// </summary>
    public void Initialize(Node3D transformTarget, Camera3D targetCamera, Shaker3D? shaker)
    {
        _transformTarget = transformTarget;
        _targetCamera = targetCamera;
        _shaker = shaker;

        if (_transformTarget != null)
        {
            _cameraRotation = new Vector2(_transformTarget.Rotation.X, _transformTarget.Rotation.Y);
        }

        UpdateRotationLimits();
    }

    public void UpdateRotationLimits()
    {
        _minPitchRad = Mathf.DegToRad(MinPitch);
        _maxPitchRad = Mathf.DegToRad(MaxPitch);
        _maxYawRad = Mathf.DegToRad(MaxYaw);
    }

    public void Update(Vector2 mouseDelta, float delta)
    {
        _shaker?.Update(delta);
        UpdateCameraRotation(mouseDelta);
        ApplyFinalTransformations();
    }

    public void SetRotation(Vector3 eulerAngles)
    {
        _cameraRotation.X = eulerAngles.X;
        _cameraRotation.Y = eulerAngles.Y;
        ApplyCameraRotationLimits();
        ApplyFinalTransformations();
    }

    public void AddRotation(Vector3 rotationDelta)
    {
        _cameraRotation.X += rotationDelta.X;
        _cameraRotation.Y += rotationDelta.Y;
    }

    private void UpdateCameraRotation(Vector2 mouseDelta)
    {
        // Формула:
        // [Raw Input] * [Global User Setting] * [Individual Node Speed] * [Dynamic Modifiers (Zoom)]

        // 1. Получаем глобальную настройку (например, 1.0 по дефолту)
        float globalSens = GlobalSettings.Instance.MouseSensitivity;

        // 2. Инверсия Y (если включена в настройках)
        float invertYMultiplier = GlobalSettings.Instance.InvertY ? -1.0f : 1.0f;

        // 3. Считаем итоговый скаляр
        float finalSensitivity = BaseRawSensitivity * globalSens * NodeSensitivity * DynamicSensitivity;

        _cameraRotation.Y -= mouseDelta.X * finalSensitivity;

        // Применяем инверсию к оси Pitch (X)
        _cameraRotation.X -= mouseDelta.Y * finalSensitivity * invertYMultiplier;

        ApplyCameraRotationLimits();

        // GD.Print($"Frame: {Engine.GetProcessFrames()}, Delta: {mouseDelta}, FinalSens: {finalSensitivity}");
    }

    private void ApplyCameraRotationLimits()
    {
        _cameraRotation.X = Mathf.Clamp(_cameraRotation.X, _minPitchRad, _maxPitchRad);
        if (_maxYawRad >= 0)
        {
            _cameraRotation.Y = Mathf.Clamp(_cameraRotation.Y, -_maxYawRad, _maxYawRad);
        }
    }

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

    private Vector3 CalculateFinalPosition()
    {
        if (!GodotObject.IsInstanceValid(_shaker) || !GodotObject.IsInstanceValid(_targetCamera))
            return Vector3.Zero;

        var (currentPositionOffset, _) = _shaker!.GetCurrentShakeOffsets();
        Vector3 basePosition = _targetCamera!.Position - _lastPositionOffset;
        Vector3 newPosition = basePosition + currentPositionOffset;
        _lastPositionOffset = currentPositionOffset;
        return newPosition;
    }
}