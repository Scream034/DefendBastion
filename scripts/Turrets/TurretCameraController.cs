using Godot;
using Game.Interfaces;
using Game.Player;
using System.Threading.Tasks; // Для CameraController и RotationLimits

namespace Game.Turrets;

/// <summary>
/// Компонент, отвечающий за управление камерой и прицеливанием турели.
/// Реализует ICameraController, позволяя PlayerInputManager передавать ему управление.
/// </summary>
public partial class TurretCameraController : Node, ICameraController
{
    [ExportGroup("Required Nodes")]
    [Export] private Camera3D _camera;
    [Export] private Node3D _turretYaw;
    [Export] private Node3D _turretPitch;

    [ExportGroup("Aiming Properties")]
    [Export(PropertyHint.Range, "0.1, 5.0, 0.1")] public float SensitivityMultiplier { get; private set; } = 1.0f;
    [Export(PropertyHint.Range, "1, 20, 0.1")] private float _aimSpeed = 5f;

    [ExportGroup("Rotation Limits (Degrees)")]
    [Export(PropertyHint.Range, "-90, 0, 1")] public float MinPitch { get; private set; } = -20f;
    [Export(PropertyHint.Range, "0, 90, 1")] public float MaxPitch { get; private set; } = 45f;
    [Export(PropertyHint.Range, "-1, 180, 1")] public float MaxYaw { get; private set; } = 90f;

    // Кэшированные значения в радианах
    private float _minPitchRad, _maxPitchRad, _maxYawRad;
    private ControllableTurret _ownerTurret;

    // Тряска камеры
    private FastNoiseLite _shakeNoise = new();
    private float _shakeStrength = 0f;
    private ulong _shakeSeed;

    public override void _Ready()
    {
        // Находим родительскую турель
        _ownerTurret = GetOwner<ControllableTurret>();
        if (_ownerTurret == null)
        {
            GD.PushError("TurretCameraController должен быть дочерним узлом ControllableTurret!");
            SetProcess(false);
            return;
        }

        // Кэшируем радианы
        _minPitchRad = Mathf.DegToRad(MinPitch);
        _maxPitchRad = Mathf.DegToRad(MaxPitch);
        _maxYawRad = Mathf.DegToRad(MaxYaw);

        _shakeNoise.Seed = (int)GD.Randi();
        _shakeNoise.Frequency = 0.5f;

        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        // Вся логика вращения уходит в HandleMouseInput
        // Оставляем только логику, которая должна работать постоянно, например, тряску.
        if (_shakeStrength > 0)
        {
            _shakeSeed++;
            var amount = _shakeNoise.GetNoise2D(_shakeSeed, _shakeSeed) * _shakeStrength;
            _camera.Rotation = new Vector3(amount, amount, amount);
        }
    }
    public override void _PhysicsProcess(double delta)
    {
        // Эта логика наведения теперь принадлежит этому классу, а не PlayerControllableTurret

        // 1. Получаем глобальную ориентацию камеры игрока (которая теперь наша)
        Basis targetGlobalBasis = _camera.GlobalTransform.Basis;

        // 2. Преобразуем ее в локальную систему координат турели
        Basis localBasis = _ownerTurret.GlobalTransform.Basis.Inverse() * targetGlobalBasis;

        // 3. Извлекаем углы Эйлера
        Vector3 targetEuler = localBasis.GetEuler();

        // 4. Ограничиваем углы
        float targetYaw = _maxYawRad < 0 ? targetEuler.Y : Mathf.Clamp(targetEuler.Y, -_maxYawRad, _maxYawRad);
        float targetPitch = Mathf.Clamp(targetEuler.X, _minPitchRad, _maxPitchRad);

        // 5. Плавно интерполируем
        float fDelta = (float)delta;
        _turretYaw.Rotation = _turretYaw.Rotation with { Y = Mathf.LerpAngle(_turretYaw.Rotation.Y, targetYaw, _aimSpeed * fDelta) };
        _turretPitch.Rotation = _turretPitch.Rotation with { X = Mathf.LerpAngle(_turretPitch.Rotation.X, targetPitch, _aimSpeed * fDelta) };
    }

    #region ICameraController Implementation

    public void Activate()
    {
        SetProcess(true);
        _camera.Current = true;
    }

    public void Deactivate()
    {
        SetProcess(false);
        _camera.Current = false;
    }

    public void HandleMouseInput(Vector2 mouseDelta)
    {
        // Просто вращаем нашу камеру. Логика в _Process сделает все остальное.
        // Это позволяет применять шейдеры, отдачу и т.д. именно к камере турели.
        var rotation = _camera.Rotation;
        rotation.Y -= mouseDelta.X * CameraController.MouseSensitivityMultiplier * SensitivityMultiplier;
        rotation.X -= mouseDelta.Y * CameraController.MouseSensitivityMultiplier * SensitivityMultiplier;

        // Ограничиваем вращение самой камеры, чтобы она не "вылетала" за пределы вида
        rotation.X = Mathf.Clamp(rotation.X, Mathf.DegToRad(-89), Mathf.DegToRad(89));

        _camera.Rotation = rotation;
    }

    public Camera3D GetCamera() => _camera;

    public IOwnerCameraController GetCameraOwner() => (IOwnerCameraController)_ownerTurret;

    #endregion

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
        _camera.Rotation = Vector3.Zero;
    }
}