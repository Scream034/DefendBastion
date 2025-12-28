#nullable enable

using Game.Interfaces;
using Game.Components;
using Godot;
using Game.Components.Nodes;
using Game.Singletons;
using Game.UI;
using Game.Entity; // Не забудь подключить неймспейс с RobotBus

namespace Game.Player;

public sealed partial class PlayerHead : Node3D, ICameraController
{
    private CameraOperator? _cameraOperator;

    [ExportGroup("Components")]
    [Export] public Camera3D? Camera { get; private set; }

    [ExportSubgroup("Raycasts")]
    [Export] private RayCast3D? _interactionRay; // Короткий луч для рук (E)
    [Export] private RayCast3D? _scannerRay;     // Длинный луч для глаз (Прицел/UI)

    [Export] private Shaker3D? _shaker;

    [ExportGroup("Mouse look")]
    [Export] public float Sensitivity { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch = -80f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch = 80f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw = -1f;

    public IInteractable? CurrentInteractable { get; private set; }

    // Временное хранилище для лимитов
    private (float, float, float)? _beforeRotationLimits;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;

        _cameraOperator = new();

#if DEBUG
        if (Camera == null) GD.PushError($"[{Name}] Для PlayerHead не назначена Camera3D.");
        if (_interactionRay == null) GD.PushError($"[{Name}] Для PlayerHead не назначен Interaction RayCast3D.");
        if (_scannerRay == null) GD.PushWarning($"[{Name}] Scanner RayCast3D не назначен! Прицел не будет реагировать на дистанцию.");
        if (_shaker == null) GD.PushError($"[{Name}] Для PlayerHead не назначен Shaker3D.");
#endif

        // Инициализируем оператора
        if (_cameraOperator != null && Camera != null)
        {
            _cameraOperator.NodeSensitivity = Sensitivity;
            _cameraOperator.MinPitch = MinPitch;
            _cameraOperator.MaxPitch = MaxPitch;
            _cameraOperator.MaxYaw = MaxYaw;
            _cameraOperator.Initialize(this, Camera, _shaker);

            Camera.Fov = GlobalSettings.Instance.FieldOfView;
            GlobalSettings.Instance.OnFovChanged += OnFovChanged;
        }
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
        if (GetParent() is LocalPlayer)
        {
            CheckForInteraction();
            UpdateScannerData(); // <-- Новая логика сканера
        }
        else
        {
            CurrentInteractable = null;
        }
    }

    #region Scanner Logic

    /// <summary>
    /// Сканирует пространство перед игроком для UI прицела.
    /// </summary>
    private void UpdateScannerData()
    {
        // Если луча нет, ничего не делаем
        if (_scannerRay == null) return;

        bool foundTarget = false;
        string targetInfo = "";
        float distance;

        // 1. Проверяем коллизию длинного луча
        if (_scannerRay.IsColliding())
        {
            var collisionPoint = _scannerRay.GetCollisionPoint();
            // Вычисляем точную дистанцию от головы до точки попадания
            distance = GlobalPosition.DistanceTo(collisionPoint);

            var collider = _scannerRay.GetCollider();

            // 2. Определение "Цели" (Враг/Союзник)
            // Здесь можно проверять группы, интерфейсы или слои
            if (collider is LivingEntity entity)
            {
                if (entity.IsHostile(LocalPlayer.Instance))
                {
                    foundTarget = true;
                    targetInfo = $"{entity.GetRid()}={entity.ID}";
                }
            }
        }
        else
        {
            // Если луч смотрит в небо/пустоту -> дистанция макс. длина луча или просто большое число
            distance = _scannerRay.TargetPosition.Length();
            // TargetPosition.Z обычно отрицательный (вперед), берем длину вектора
        }

        // 3. Отправляем данные в UI
        // SmartReticle использует distance для alpha-канала и размера точки
        RobotBus.PublishScanData(foundTarget, targetInfo, distance);
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

    public async void AddRecoilAsync(Vector3 direction, float strength, float recoilTime = 0.1f)
    {
        if (_cameraOperator == null) return;

        var tween = GetTree().CreateTween();
        tween.TweenMethod(Callable.From<float>(f =>
        {
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
        _cameraOperator.UpdateRotationLimits();

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
        Rotation = originalRotation;
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
            Camera.Fov = newFov;
        }
    }
}