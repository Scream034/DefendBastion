using System;
using System.Threading.Tasks;
using Game.Interfaces;
using Godot;

namespace Game.Player;

public sealed partial class PlayerHead : Node3D
{
    [ExportGroup("Mouse look")]
    [Export] public Camera3D Camera { get; private set; }
    [Export] public float Sensitivity { get; set; } = 1.5f;
    [Export] public float RotationSpeed { get; set; } = 1f;
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch = -80f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch = 80f;

    /// <summary>
    /// Максимальный угол поворота вокруг оси Y
    /// Если указать -1, то угол не будет ограничен
    /// </summary>
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw = -1f;

    [ExportGroup("Interaction")]
    [Export]
    private RayCast3D _interactionRay;

    public IInteractable CurrentInteractable { get; private set; }

    private CameraController _cameraController;
    private Vector2 _mouseDelta = Vector2.Zero;

    private RotationLimits _beforeRotationLimits;

    private FastNoiseLite _shakeNoise = new();
    private float _shakeStrength = 0f;
    private ulong _shakeSeed;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _cameraController = new CameraController(this, new()
        {
            MinPitch = MinPitch,
            MaxPitch = MaxPitch,
            MaxYaw = MaxYaw
        });
        
        _shakeNoise.Seed = (int)GD.Randi();
        _shakeNoise.Frequency = 0.5f;

#if DEBUG
        if (Camera == null) GD.PushError("Для PlayerHead не назначена Camera3D.");
        if (_interactionRay == null) GD.PushError("Для PlayerHead не был назначен RayCast3D для взаимодействия.");
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
        _cameraController.HandleMouseLook(_mouseDelta, Sensitivity, RotationSpeed);
        _mouseDelta = Vector2.Zero;
        
        if (_shakeStrength > 0)
        {
            _shakeSeed++;
            var amount = _shakeNoise.GetNoise2D(_shakeSeed, _shakeSeed) * _shakeStrength;
            Camera.Rotation = new Vector3(amount, amount, amount);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Проверяем интерактивные объекты, только если мы не в турели.
        // Надежный способ проверить это - посмотреть, является ли родитель игроком.
        if (GetParent() is Player)
        {
            CheckForInteraction();
        }
        else
        {
            CurrentInteractable = null;
        }
    }
    
    /// <summary>
    /// Визуальная тряска камеры игрока
    /// </summary>
    /// <param name="duration">В секундах</param>
    /// <param name="strength">Чем больше, тем сильнее</param>
    public async void ShakeAsync(float duration, float strength)
    {
        _shakeStrength = strength;
        await Task.Delay((int)(duration * 1000));
        _shakeStrength = 0f;
        Camera.Rotation = Vector3.Zero;
    }

    public async void AddRecoilAsync(Vector3 direction, float strength, float recoilTime = 0.1f)
    {
        var recoilSpeed = strength / recoilTime;
        
        var tween = GetTree().CreateTween();
        tween.TweenMethod(Callable.From<float>(f =>
        {
            _cameraController.AddRotation(direction * f);
        }), 0f, strength, recoilTime).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        
        await ToSignal(tween, Tween.SignalName.Finished);
        
        var returnTween = GetTree().CreateTween();
        returnTween.TweenMethod(Callable.From<float>(f =>
        {
            _cameraController.AddRotation(-direction * f);
        }), 0f, strength, recoilTime * 2).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
    }

    public bool SetTempRotationLimits(RotationLimits limits)
    {
        if (_beforeRotationLimits != null) return false;
        _beforeRotationLimits = _cameraController.Limits;
        _cameraController.Limits = limits;
        return true;
    }

    public bool RestoreRotationLimits()
    {
        if (_beforeRotationLimits == null) return false;
        _cameraController.Limits = _beforeRotationLimits;
        _beforeRotationLimits = null;
        return true;
    }

    public void TryRotateHeadTowards(Vector3 position, Vector3? up = null)
    {
        _mouseDelta = Vector2.Zero;
        var originalRotation = Rotation;
        LookAt(position, up ?? Vector3.Up);
        var targetRotation = Rotation;
        Rotation = originalRotation;

        _cameraController.SetRotation(targetRotation);
    }

    public void TryRotateHeadTowards(Node3D target, Vector3? up = null)
    {
        TryRotateHeadTowards(target.GlobalPosition, up);
    }

    private void CheckForInteraction()
    {
        if (_interactionRay.IsColliding())
        {
            var collider = _interactionRay.GetCollider();
            if (collider is IInteractable interactable)
            {
                if (interactable == CurrentInteractable) return;
                CurrentInteractable = interactable;
            }
            else
            {
                CurrentInteractable = null;
            }
        }
        else
        {
            CurrentInteractable = null;
        }
    }

    internal void ShakeAsync(double v1, float v2)
    {
        throw new NotImplementedException();
    }

}
