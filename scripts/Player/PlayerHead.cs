#nullable enable

using Game.Interfaces;
using Game.Components;
using Godot;
using Game.Components.Nodes;
using Game.Singletons;

namespace Game.Player;

public sealed partial class PlayerHead : Node3D, ICameraController
{
    public static PlayerHead Instance { get; private set; } = null!;

    private CameraOperator? _cameraOperator;

    [ExportGroup("Components")]
    [Export] public Camera3D? Camera { get; private set; }
    [Export] private RayCast3D? _interactionRay;
    [Export] private Shaker3D? _shaker;

    [ExportGroup("Mouse look")]
    [Export] public float Sensitivity { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch = -80f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch = 80f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw = -1f;

    public IInteractable? CurrentInteractable { get; private set; }

    // Временное хранилище для лимитов
    private (float, float, float)? _beforeRotationLimits;

    public PlayerHead()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;

        _cameraOperator = new();

#if DEBUG
        if (Camera == null) GD.PushError("Для PlayerHead не назначена Camera3D.");
        if (_interactionRay == null) GD.PushError("Для PlayerHead не был назначен RayCast3D для взаимодействия.");
        if (_shaker == null) GD.PushError("Для PlayerHead не назначен Shaker3D для тряски камеры.");
#endif

        // Инициализируем оператора, передавая ему все необходимые ссылки и настройки
        if (_cameraOperator != null && Camera != null)
        {
            _cameraOperator.NodeSensitivity = Sensitivity;
            _cameraOperator.MinPitch = MinPitch;
            _cameraOperator.MaxPitch = MaxPitch;
            _cameraOperator.MaxYaw = MaxYaw;
            // вращаем сам узел PlayerHead (this), а позицию меняем у дочерней камеры (Camera)
            _cameraOperator.Initialize(this, Camera, _shaker);
            Camera.Fov = GlobalSettings.Instance.FieldOfView;

            // Подписываемся на изменения
            GlobalSettings.Instance.OnFovChanged += OnFovChanged;
        }
    }

    public override void _ExitTree()
    {
        // Обязательно отписываемся, чтобы избежать утечек памяти (хотя для автолоадов это не так критично, но это хорошая привычка)
        if (GlobalSettings.Instance != null)
        {
            GlobalSettings.Instance.OnFovChanged -= OnFovChanged;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GetParent() is Player)
        {
            CheckForInteraction();
        }
        else
        {
            CurrentInteractable = null;
        }
    }

    #region ICameraController Implementation

    public void Activate()
    {
        SetPhysicsProcess(true);
        if (Camera != null) Camera.Current = true;
    }

    public void Deactivate()
    {
        SetPhysicsProcess(false);
        if (Camera != null) Camera.Current = false;
        CurrentInteractable = null;
    }

    public void HandleMouseInput(Vector2 mouseDelta, float delta)
    {
        // Вся логика теперь в одной строке - делегируем оператору
        _cameraOperator?.Update(mouseDelta, delta);
    }

    public void HandleMouseInput(Vector2 mouseDelta)
    {
        // Вызываем основную версию с актуальным delta
        HandleMouseInput(mouseDelta, (float)GetProcessDeltaTime());
    }

    public Camera3D GetCamera() => Camera!;

    public IOwnerCameraController GetCameraOwner() => GetParent<Player>();

    public void ApplyShake(float duration, float strength)
    {
        GD.Print($"{Name} Try apply shake: {duration}, {strength}");
        _shaker?.StartShake(duration, strength, false);
    }

    public void SetSensitivityModifier(float modifier)
    {
        if (_cameraOperator != null)
        {
            _cameraOperator.DynamicSensitivity = modifier;
        }
    }

    #endregion

    public async void AddRecoilAsync(Vector3 direction, float strength, float recoilTime = 0.1f)
    {
        if (_cameraOperator == null) return;

        var tween = GetTree().CreateTween();
        tween.TweenMethod(Callable.From<float>(f =>
        {
            // Используем новый метод оператора
            _cameraOperator.AddRotation(direction * f);
        }), 0f, strength, recoilTime).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        await ToSignal(tween, Tween.SignalName.Finished);

        var returnTween = GetTree().CreateTween();
        returnTween.TweenMethod(Callable.From<float>(f =>
        {
            _cameraOperator.AddRotation(-direction * f);
        }), 0f, strength, recoilTime * 2).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
    }

    public bool SetTempRotationLimits(float minPitch, float maxPitch, float maxYaw)
    {
        if (_cameraOperator == null || _beforeRotationLimits != null) return false;

        _beforeRotationLimits = (_cameraOperator.MinPitch, _cameraOperator.MaxPitch, _cameraOperator.MaxYaw);

        _cameraOperator.MinPitch = minPitch;
        _cameraOperator.MaxPitch = maxPitch;
        _cameraOperator.MaxYaw = maxYaw;
        _cameraOperator.UpdateRotationLimits(); // Сообщаем оператору об изменениях

        return true;
    }

    public bool RestoreRotationLimits()
    {
        if (_cameraOperator == null || _beforeRotationLimits == null) return false;

        var (minP, maxP, maxY) = _beforeRotationLimits.Value;
        _cameraOperator.MinPitch = minP;
        _cameraOperator.MaxPitch = maxP;
        _cameraOperator.MaxYaw = maxY;
        _cameraOperator.UpdateRotationLimits();

        _beforeRotationLimits = null;
        return true;
    }

    public void TryRotateHeadTowards(Vector3 position, Vector3? up = null)
    {
        var originalRotation = Rotation;
        LookAt(position, up ?? Vector3.Up);
        var targetRotation = Rotation;
        Rotation = originalRotation; // Возвращаем, чтобы не было визуального скачка

        // Делегируем установку поворота оператору
        _cameraOperator?.SetRotation(targetRotation);
    }

    public void TryRotateHeadTowards(Node3D target, Vector3? up = null)
    {
        TryRotateHeadTowards(target.GlobalPosition, up);
    }

    private void CheckForInteraction()
    {
        if (_interactionRay == null) return;

        if (_interactionRay.IsColliding())
        {
            var collider = _interactionRay.GetCollider();
            if (collider is IInteractable interactable)
            {
                if (interactable != CurrentInteractable)
                {
                    CurrentInteractable = interactable;
                }
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

    private void OnFovChanged(float newFov)
    {
        if (Camera != null)
        {
            // Здесь можно добавить Tween для плавного изменения, если хочется красоты
            Camera.Fov = newFov;
        }
    }
}