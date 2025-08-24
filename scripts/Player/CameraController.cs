using Godot;

namespace Game.Player;

public sealed class CameraController
{
    public const float MouseSensitivityMultiplier = 0.0005f;

    private readonly Node3D _node;

    public RotationLimits Limits;

    private Vector3 _rotation;

    public CameraController(Node3D node, RotationLimits limits)
    {
        _node = node;
        _rotation = node.Rotation;
        Limits = limits;
    }

    public void HandleMouseLook(Vector2 mouseDelta, float sensitivity, float rotationSpeed)
    {
        if (mouseDelta != Vector2.Zero)
        {
            // Горизонтальное вращение (вокруг оси Y)
            _rotation.Y -= mouseDelta.X * MouseSensitivityMultiplier * sensitivity * rotationSpeed;

            // Вертикальное вращение (вокруг оси X)
            _rotation.X -= mouseDelta.Y * MouseSensitivityMultiplier * sensitivity * rotationSpeed;

            // Ограничение вертикального вращения
            Limits.Handle(ref _rotation);
        }
        _node.Rotation = _rotation;
    }

    public void SetRotation(Vector3 newRotation)
    {
        _rotation = newRotation;
        Limits.Handle(ref _rotation);
        _node.Rotation = _rotation;
    }

    public void AddRotation(Vector3 rotation)
    {
        _rotation += rotation;
        Limits.Handle(ref _rotation);
        _node.Rotation = _rotation;
    }
}

public sealed class RotationLimits
{
    private float _minRotationX;
    private float _maxRotationX;
    private float _maxRotationY;

    public float MinPitch { get => _minRotationX; set => _minRotationX = Mathf.DegToRad(value); }
    public float MaxPitch { get => _maxRotationX; set => _maxRotationX = Mathf.DegToRad(value); }
    public float MaxYaw { get => _maxRotationY; set => _maxRotationY = Mathf.DegToRad(value); }

    public void Handle(ref Vector3 rotation)
    {
        rotation.X = Mathf.Clamp(rotation.X, _minRotationX, _maxRotationX);
        if (_maxRotationY >= 0)
        {
            rotation.Y = Mathf.Clamp(rotation.Y, -_maxRotationY, _maxRotationY);
        }
    }
}