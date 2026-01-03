#nullable enable

using Game.Interfaces;
using Game.Components;
using Godot;
using Game.Components.Nodes;
using Game.Singletons;
using Game.UI;
using Game.Entity;

namespace Game.Player;

/// <summary>
/// Управляет "головой" игрока: камерой, определением взаимодействия (Raycast) и сканированием окружения.
/// Реализует интерфейс контроллера камеры для передачи ввода мыши.
/// </summary>
public sealed partial class PlayerHead : Node3D, ICameraController
{
    #region Configuration

    [ExportGroup("Components")]
    [Export] public Camera3D Camera { get; private set; } = null!;

    [ExportSubgroup("Sensors")]
    /// <summary>Луч для взаимодействия с объектами (руки/дистанция действия).</summary>
    [Export] private RayCast3D _interactionRay = null!;
    /// <summary>Луч для сканирования дальних объектов (глаза/информация UI).</summary>
    [Export] private RayCast3D _scannerRay = null!;

    [ExportSubgroup("Feedback")]
    [Export] private Shaker3D _shaker = null!;

    [ExportGroup("Camera Control")]
    [Export] public float Sensitivity { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch = -80f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch = 80f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw = -1f;

    #endregion

    #region State

    public IInteractable? CurrentInteractable { get; private set; }

    private CameraOperator? _cameraOperator;

    // Хранилище для восстановления ограничений вращения (Memento pattern lite)
    private (float minP, float maxP, float maxY)? _rotationLimitsBackup;

    #endregion

    #region Lifecycle

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;

        ValidateDependencies();
        InitializeCameraOperator();
    }

    public override void _ExitTree()
    {
        if (GlobalSettings.Instance != null)
        {
            GlobalSettings.Instance.OnFovChanged -= OnFovChanged;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Обрабатываем сенсоры только если голова принадлежит локальному игроку
        if (GetParent() is LocalPlayer)
        {
            ProcessInteractionRay();
            ProcessScannerRay();
        }
        else
        {
            CurrentInteractable = null;
        }
    }

    #endregion

    #region Logic Implementation

    private void ValidateDependencies()
    {
#if DEBUG
        if (Camera == null) GD.PushError($"[{Name}] Camera3D не назначена!");
        if (_interactionRay == null) GD.PushError($"[{Name}] Interaction RayCast3D не назначен!");
        if (_scannerRay == null) GD.PushWarning($"[{Name}] Scanner RayCast3D не назначен! Дальномер не будет работать.");
        if (_shaker == null) GD.PushError($"[{Name}] Shaker3D не назначен!");
#endif
    }

    private void InitializeCameraOperator()
    {
        _cameraOperator = new CameraOperator
        {
            NodeSensitivity = Sensitivity,
            MinPitch = MinPitch,
            MaxPitch = MaxPitch,
            MaxYaw = MaxYaw
        };

        if (Camera != null)
        {
            _cameraOperator.Initialize(this, Camera, _shaker);
            Camera.Fov = GlobalSettings.Instance.FieldOfView;
            GlobalSettings.Instance.OnFovChanged += OnFovChanged;
        }
    }

    private void ProcessInteractionRay()
    {
        if (_interactionRay == null) return;

        if (_interactionRay.IsColliding() && _interactionRay.GetCollider() is IInteractable interactable)
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

    /// <summary>
    /// Логика сканера: определяет дистанцию и тип сущности перед игроком.
    /// Данные отправляются в RobotBus для UI.
    /// </summary>
    private void ProcessScannerRay()
    {
        if (_scannerRay == null) return;

        bool foundTarget = false;
        string targetInfo = "";
        float distance;

        if (_scannerRay.IsColliding())
        {
            var collisionPoint = _scannerRay.GetCollisionPoint();
            distance = GlobalPosition.DistanceTo(collisionPoint);

            var collider = _scannerRay.GetCollider();

            // Идентификация цели
            if (collider is LivingEntity entity && entity.IsHostile(LocalPlayer.Instance))
            {
                foundTarget = true;
                // Осторожно: String Interpolation в PhysicsProcess создает мусор (GC pressure).
                // В идеале GetRid() и ID должны кэшироваться или передаваться как объекты, а не строки.
                targetInfo = $"{entity.GetRid()}={entity.ID}";
            }
        }
        else
        {
            // Берем длину вектора TargetPosition как макс. дальность
            distance = _scannerRay.TargetPosition.Length();
        }

        RobotBus.PublishScanData(foundTarget, targetInfo, distance);
    }

    private void OnFovChanged(float newFov)
    {
        if (Camera != null) Camera.Fov = newFov;
    }

    #endregion

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
        _cameraOperator?.Update(mouseDelta, delta);
    }

    public void HandleMouseInput(Vector2 mouseDelta)
    {
        HandleMouseInput(mouseDelta, (float)GetProcessDeltaTime());
    }

    public Camera3D GetCamera() => Camera!;

    public IOwnerCameraController GetCameraOwner() => GetParent<LocalPlayer>();

    public void ApplyShake(float duration, float strength)
    {
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

    #region Head Control API

    /// <summary>
    /// Добавляет процедурную отдачу (рывок камеры вверх/в сторону с возвратом).
    /// </summary>
    public async void AddRecoilAsync(Vector3 direction, float strength, float recoilTime = 0.1f)
    {
        if (_cameraOperator == null) return;

        // Фаза удара
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(val => _cameraOperator.AddRotation(direction * val)),
            0f, strength, recoilTime)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);

        await ToSignal(tween, Tween.SignalName.Finished);

        // Фаза возврата (в 2 раза медленнее)
        var returnTween = CreateTween();
        returnTween.TweenMethod(Callable.From<float>(val => _cameraOperator.AddRotation(-direction * val)),
            0f, strength, recoilTime * 2f)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    /// <summary>
    /// Временно переопределяет лимиты вращения камеры (например, при посадке в транспорт).
    /// </summary>
    public bool SetTempRotationLimits(float minPitch, float maxPitch, float maxYaw)
    {
        if (_cameraOperator == null || _rotationLimitsBackup != null) return false;

        // Сохраняем текущие значения
        _rotationLimitsBackup = (_cameraOperator.MinPitch, _cameraOperator.MaxPitch, _cameraOperator.MaxYaw);

        // Применяем новые
        _cameraOperator.MinPitch = minPitch;
        _cameraOperator.MaxPitch = maxPitch;
        _cameraOperator.MaxYaw = maxYaw;
        _cameraOperator.UpdateRotationLimits();

        return true;
    }

    /// <summary>
    /// Восстанавливает стандартные лимиты вращения.
    /// </summary>
    public bool RestoreRotationLimits()
    {
        if (_cameraOperator == null || _rotationLimitsBackup == null) return false;

        var (minP, maxP, maxY) = _rotationLimitsBackup.Value;
        _cameraOperator.MinPitch = minP;
        _cameraOperator.MaxPitch = maxP;
        _cameraOperator.MaxYaw = maxY;
        _cameraOperator.UpdateRotationLimits();

        _rotationLimitsBackup = null;
        return true;
    }

    public void TryRotateHeadTowards(Vector3 globalPosition, Vector3? up = null)
    {
        // Трюк: используем LookAt Node3D, чтобы вычислить кватернион, 
        // затем извлекаем вращение и применяем к оператору, возвращая Node3D в исходное состояние.
        // Это нужно, так как CameraOperator хранит свои углы (yaw/pitch) отдельно.

        var originalRotation = Rotation;
        LookAt(globalPosition, up ?? Vector3.Up);
        var targetRotation = Rotation;
        Rotation = originalRotation;

        _cameraOperator?.SetRotation(targetRotation);
    }

    public void TryRotateHeadTowards(Node3D target, Vector3? up = null)
    {
        TryRotateHeadTowards(target.GlobalPosition, up);
    }

    public float GetScannerDistance()
    {
        if (_scannerRay == null) return 0f;

        return _scannerRay.IsColliding()
            ? GlobalPosition.DistanceTo(_scannerRay.GetCollisionPoint())
            : _scannerRay.TargetPosition.Length();
    }

    public float GetScannerMaxDistance()
    {
        return _scannerRay != null ? _scannerRay.TargetPosition.Length() : 0f;
    }

    #endregion
}