using Game.Entity;
using Godot;

namespace Game.Player;

public partial class TurretController : Camera3D
{
    #region Экспортируемые Поля
    [ExportGroup("Mouse look")]
    [Export] public float Sensitivity { get; set; } = 0.5f;
    [Export] public float MouseSensitivityCoefficient { get; set; } = 1200f;
    [Export] public float RotationSpeed { get; set; } = 1f;
    [Export(PropertyHint.Range, "-90, 0, 1")] private float _minRotationX = -80f;
    [Export(PropertyHint.Range, "0, 90, 1")] private float _maxRotationX = 80f;

    [ExportGroup("Shooting")]
    [Export] public int Damage { get; set; } = 50;
    [Export(PropertyHint.Range, "0.1, 2, 0.1")] public float FireRate { get; set; } = 0.5f;
    [Export] private RayCast3D _raycast;
    [Export] private AudioStreamPlayer3D _fireSound;
    [Export] private GpuParticles3D _impactEffectPrefab;

    [ExportGroup("Camera shake")]
    [Export(PropertyHint.Range, "0, 1, 0.01")] private float _shakeStrength = 0.05f;
    [Export(PropertyHint.Range, "0, 1, 0.1")] private float _shakeDuration = 0.2f;
    [Export(PropertyHint.Range, "1, 100, 1")] private float _noiseSpeed = 50f;

    [ExportGroup("Recoil")]
    [Export] private float _recoilUpAmount = 0.02f;
    [Export] private float _recoilSideAmount = 0.01f;
    [Export] private float _recoilRecoverySpeed = 10f;
    [Export] private float _maxRecoilPitch = -0.5f;
    #endregion

    private Vector3 _rotation = Vector3.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;

    // Эффекты и таймеры
    private float _shakeTimer = 0f;
    private FastNoiseLite _noise;
    private float _noiseY = 0;
    private Vector3 _recoilOffset = Vector3.Zero;
    private float _fireCooldown = 0f;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 2f,
            Seed = (int)GD.Randi()
        };
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

        UpdateCooldown(fDelta);
        HandleInput();
        UpdateShake(fDelta);
        UpdateRecoil(fDelta);

        Rotation = _rotation + _recoilOffset;
    }

    private void HandleInput()
    {
        // Применяем движение мыши
        if (_mouseDelta != Vector2.Zero)
        {
            MoveHorizontal(_mouseDelta.X);
            MoveVertical(_mouseDelta.Y);
            ClampVertical();
            _mouseDelta = Vector2.Zero;
        }

        // Обрабатываем стрельбу
        if (Input.IsActionJustPressed("fire") && _fireCooldown <= 0)
        {
            Fire();
            _fireCooldown = FireRate;
        }
    }

    private void Fire()
    {
        _fireSound.Play();
        ShakeCamera();
        ApplyRecoil();

        if (_raycast.IsColliding())
        {
            if (_raycast.GetCollider() is LivingEntity entity)
            {
                entity.Damage(Damage);
            }

            if (_impactEffectPrefab != null)
            {
                GpuParticles3D instance = (GpuParticles3D)_impactEffectPrefab.Duplicate();
                Constants.Root.AddChild(instance);
                instance.GlobalPosition = _raycast.GetCollisionPoint();
                instance.Emitting = true;
                instance.Finished += instance.QueueFree;
            }
        }
    }

    #region Эффекты
    private void ShakeCamera()
    {
        _shakeTimer = _shakeDuration;
    }

    private void ApplyRecoil()
    {
        _recoilOffset.X -= _recoilUpAmount;
        _recoilOffset.Y += GD.RandRange(-1, 1) * _recoilSideAmount;
        _recoilOffset.X = Mathf.Max(_recoilOffset.X, _maxRecoilPitch);
    }
    #endregion

    #region Обновления в кадре
    private void UpdateCooldown(float delta)
    {
        if (_fireCooldown > 0)
        {
            _fireCooldown -= delta;
        }
    }

    private void UpdateShake(float delta)
    {
        if (_shakeTimer <= 0) return;

        _shakeTimer -= delta;
        if (_shakeTimer <= 0)
        {
            HOffset = VOffset = 0;
        }
        else
        {
            float remaining = _shakeTimer / _shakeDuration;
            float intensity = _shakeStrength * remaining * remaining;
            _noiseY += delta * _noiseSpeed;
            HOffset = _noise.GetNoise2D(0, _noiseY) * intensity;
            VOffset = _noise.GetNoise2D(100, _noiseY) * intensity;
        }
    }

    private void UpdateRecoil(float delta)
    {
        _recoilOffset = _recoilOffset.Lerp(Vector3.Zero, delta * _recoilRecoverySpeed);
    }
    #endregion

    #region Движение
    private void MoveVertical(float deltaY)
    {
        _rotation.X -= deltaY / MouseSensitivityCoefficient * Sensitivity * RotationSpeed;
    }

    private void MoveHorizontal(float deltaX)
    {
        _rotation.Y -= deltaX / MouseSensitivityCoefficient * Sensitivity * RotationSpeed;
    }

    private void ClampVertical()
    {
        _rotation.X = Mathf.Clamp(_rotation.X, Mathf.DegToRad(_minRotationX), Mathf.DegToRad(_maxRotationX));
    }
    #endregion
}
