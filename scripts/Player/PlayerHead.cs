using Game.Interfaces;
using Godot;

namespace Game.Player;

[GlobalClass]
public sealed partial class PlayerHead : Node3D
{
    [ExportGroup("Mouse look")]
    [Export] public Camera3D Camera { get; private set; }
    [Export] public float Sensitivity { get; set; } = 0.5f;
    [Export] public float MouseSensitivityCoefficient { get; set; } = 1200f;
    [Export] public float RotationSpeed { get; set; } = 1f;
    [Export(PropertyHint.Range, "-90, 0, 1")] private float _minRotationX = -80f;
    [Export(PropertyHint.Range, "0, 90, 1")] private float _maxRotationX = 80f;

    [ExportGroup("Camera shake")]
    [Export(PropertyHint.Range, "1, 100, 1")] private float _noiseSpeed = 50f;

    [ExportGroup("Interaction")]
    [Export]
    private RayCast3D _interactionRay;

    public IInteractable CurrentInteractable { get; private set; }

    private Vector3 _rotation = Vector3.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;

    // Эффекты и таймеры
    private FastNoiseLite _noise;
    private float _noiseY = 0;

    // Новая логика тряски
    private float _shakeTimer = 0f;
    private float _shakeDuration = 0f;
    private float _shakeStrength = 0f;
    private bool _autoReturn = true;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 2f,
            Seed = (int)GD.Randi()
        };

#if DEBUG
        if (_interactionRay == null)
        {
            GD.PushError("Для игрока не был назначен RayCast3D для взаимодействия.");
        }
        if (Sensitivity <= 0)
        {
            GD.PushWarning($"Неверное использование чувствительности мыши: {Sensitivity}");
        }
        if (MouseSensitivityCoefficient <= 0)
        {
            GD.PushWarning($"Неверное использование коэффициента чувствительности мыши: {MouseSensitivityCoefficient}");
        }
        if (RotationSpeed <= 0)
        {
            GD.PushWarning($"Неверное использование скорости вращения: {RotationSpeed}");
        }
#endif
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion e)
        {
            _mouseDelta = e.Relative;
        }
    }

    public override void _Process(double delta)
    {
        float fDelta = (float)delta;

        HandleMouseLook();
        UpdateShake(fDelta);

        Rotation = _rotation;
    }

    public override void _PhysicsProcess(double delta)
    {
        CheckForInteraction();
    }

    /// <summary>
    /// Запускает тряску камеры.
    /// </summary>
    /// <param name="duration">Длительность тряски в секундах.</param>
    /// <param name="strength">Сила тряски.</param>
    /// <param name="autoReturn">Если true, камера вернется в исходное положение. Если false, останется смещенной.</param>
    public void Shake(float duration = 0.2f, float strength = 0.05f, bool autoReturn = true)
    {
        _shakeDuration = duration;
        _shakeStrength = strength;
        _shakeTimer = duration;
        _autoReturn = autoReturn;
    }

    private void CheckForInteraction()
    {
        if (_interactionRay.IsColliding())
        {
            var collider = _interactionRay.GetCollider();

            // Проверяем, реализует ли объект (или его родитель) интерфейс IInteractable
            if (collider is IInteractable interactable)
            {
                // Если мы уже смотрим на этот объект, ничего не делаем
                if (interactable == CurrentInteractable) return;

                CurrentInteractable = interactable;
            }
            else
            {
                // Если смотрим на что-то другое, сбрасываем
                CurrentInteractable = null;
            }
        }
        else
        {
            CurrentInteractable = null;
        }
    }

    private void HandleMouseLook()
    {
        if (_mouseDelta != Vector2.Zero)
        {
            MoveHorizontal(_mouseDelta.X);
            MoveVertical(_mouseDelta.Y);
            ClampVertical();
            _mouseDelta = Vector2.Zero;
        }
    }

    private void UpdateShake(float delta)
    {
        if (_shakeTimer <= 0) return;

        _shakeTimer -= delta;
        if (_shakeTimer <= 0)
        {
            if (_autoReturn)
            {
                Camera.HOffset = Camera.VOffset = 0;
            }
            // Если autoReturn false, смещение остается
        }
        else
        {
            // Плавное затухание в начале и в конце
            float progress = _shakeTimer / _shakeDuration;
            float intensity = _shakeStrength * (1 - Mathf.Pow(1 - progress, 2)); // EaseOutQuad

            _noiseY += delta * _noiseSpeed;
            Camera.HOffset = _noise.GetNoise2D(0, _noiseY) * intensity;
            Camera.VOffset = _noise.GetNoise2D(100, _noiseY) * intensity;
        }
    }

    private void MoveVertical(float deltaY)
    {
        _rotation.X -= deltaY / MouseSensitivityCoefficient * Sensitivity * RotationSpeed;
    }

    private void MoveHorizontal(float deltaX)
    {
        GetParent<Node3D>().RotateY(-deltaX / MouseSensitivityCoefficient * Sensitivity * RotationSpeed);
    }

    private void ClampVertical()
    {
        _rotation.X = Mathf.Clamp(_rotation.X, Mathf.DegToRad(_minRotationX), Mathf.DegToRad(_maxRotationX));
    }
}
