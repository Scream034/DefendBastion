#nullable enable

namespace Game.Components.Nodes;

using Godot;

/// <summary>
/// Предоставляет процедурные эффекты тряски для любого узла Node3D.
/// Этот компонент использует машину состояний для управления своим поведением (неактивен, трясется, возвращается)
/// и генерирует плавные смещения на основе FastNoiseLite. Он разработан для высокой производительности,
/// гибкой настройки и предсказуемого поведения.
/// </summary>
[GlobalClass]
public sealed partial class Shaker3D : Node
{
    #region Enums

    /// <summary>
    /// Определяет, какие трансформации будут подвержены тряске.
    /// </summary>
    [System.Flags]
    public enum ShakeType
    {
        Position = 1 << 0, // 1
        Rotation = 1 << 1, // 2
        Both = Position | Rotation
    }

    /// <summary>
    /// Внутренние состояния компонента для управления логикой в методе Update.
    /// </summary>
    private enum ShakerState
    {
        Inactive,
        Shaking,
        Returning
    }

    #endregion

    #region Exports

    [ExportGroup("Target")]
    [Export]
    private Node3D? _target;

    [ExportGroup("Shake Properties")]
    [Export]
    public ShakeType Type { get; private set; } = ShakeType.Both;

    [Export(PropertyHint.Range, "1.0, 100.0, 0.1")]
    public float Frequency { get; private set; } = 25.0f;
    
    [Export(PropertyHint.Range, "0.0, 5.0, 0.05")]
    public float RotationStrengthMultiplier { get; private set; } = 0.8f;

    [ExportGroup("Return Properties")]
    [Export(PropertyHint.Range, "1.0, 50.0, 0.5")]
    public float ReturnSpeed { get; private set; } = 15.0f;

    #endregion

    #region Private Fields

    // --- State Machine ---
    private ShakerState _currentState = ShakerState.Inactive;
    private readonly FastNoiseLite _noise = new();

    // --- Shake Parameters ---
    private float _duration;
    private float _strength;
    private bool _useSmoothReturn;
    private double _startTime;
    private float _elapsedTime;

    // --- Cached Base Transformations ---
    private Vector3 _basePosition;
    private Vector3 _baseRotation;

    // --- Pre-calculated Offsets ---
    private Vector3 _currentPositionOffset = Vector3.Zero;
    private Vector3 _currentRotationOffset = Vector3.Zero;

    // --- Constants ---
    private const float ReturnFinishThreshold = 0.001f;
    
    // Смещения для выборки из разных областей шума, чтобы оси двигались асинхронно
    private const float NoiseOffsetXPos = 12.34f;
    private const float NoiseOffsetYPos = 56.78f;
    private const float NoiseOffsetZPos = 90.12f;
    private const float NoiseOffsetXRot = 34.56f;
    private const float NoiseOffsetYRot = 78.90f;
    private const float NoiseOffsetZRot = 23.45f;

    #endregion

    #region Godot Lifecycle Methods

    public override void _Ready()
    {
        InitializeNoise();
        InitializeTarget();
        CacheBaseTransformations();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Запускает эффект тряски с заданными параметрами.
    /// Если шейкер уже активен, он плавно перейдет к новой тряске.
    /// </summary>
    /// <param name="duration">Продолжительность тряски в секундах.</param>
    /// <param name="strength">Интенсивность тряски. Рекомендуемые значения от 0.1 до 10.</param>
    /// <param name="smoothReturn">Использовать ли плавное возвращение в исходное положение после окончания.</param>
    public void StartShake(float duration, float strength, bool smoothReturn = true)
    {
        if (_target == null || strength <= 0 || duration <= 0) return;

        // Если шейкер был неактивен, кэшируем текущее состояние как базу для возврата.
        if (_currentState == ShakerState.Inactive)
        {
            CacheBaseTransformations();
        }

        _duration = duration;
        _strength = strength;
        _useSmoothReturn = smoothReturn;
        _startTime = Time.GetTicksMsec() / 1000.0;
        _elapsedTime = 0f;

        // Переводим машину состояний в активную фазу.
        _currentState = ShakerState.Shaking;
    }

    /// <summary>
    /// Основной метод обновления состояния шейкера. Должен вызываться каждый кадр извне
    /// (например, из CameraOperator или другого управляющего класса).
    /// </summary>
    /// <param name="delta">Время, прошедшее с предыдущего кадра.</param>
    public void Update(float delta)
    {
        switch (_currentState)
        {
            case ShakerState.Shaking:
                ProcessShakingState();
                break;
            case ShakerState.Returning:
                ProcessReturningState(delta);
                break;
            case ShakerState.Inactive:
            default:
                // Если мы неактивны, убеждаемся, что смещения равны нулю.
                if (_currentPositionOffset != Vector3.Zero || _currentRotationOffset != Vector3.Zero)
                {
                    _currentPositionOffset = Vector3.Zero;
                    _currentRotationOffset = Vector3.Zero;
                }
                break;
        }
    }

    /// <summary>
    /// Возвращает текущие вычисленные смещения для позиции и вращения.
    /// Этот метод является быстрым геттером и не выполняет вычислений.
    /// Всю логику выполняет метод Update().
    /// </summary>
    /// <returns>Кортеж со смещением для позиции и вращения.</returns>
    public (Vector3 positionOffset, Vector3 rotationOffset) GetCurrentShakeOffsets()
    {
        return (_currentPositionOffset, _currentRotationOffset);
    }

    #endregion

    #region State Processing

    /// <summary>
    /// Обрабатывает логику, когда шейкер находится в состоянии активной тряски.
    /// </summary>
    private void ProcessShakingState()
    {
        UpdateElapsedTime();
        if (_elapsedTime >= _duration)
        {
            TransitionToReturnOrInactive();
        }
        else
        {
            CalculateCurrentOffsets();
        }
    }

    /// <summary>
    /// Обрабатывает логику, когда шейкер плавно возвращается в исходное состояние.
    /// </summary>
    /// <param name="delta">Время кадра для плавного движения.</param>
    private void ProcessReturningState(float delta)
    {
        bool positionReached = !Type.HasFlag(ShakeType.Position);
        bool rotationReached = !Type.HasFlag(ShakeType.Rotation);
        float moveStep = ReturnSpeed * delta;

        if (Type.HasFlag(ShakeType.Position))
        {
            _currentPositionOffset = _currentPositionOffset.MoveToward(Vector3.Zero, moveStep);
            if (_currentPositionOffset.LengthSquared() < ReturnFinishThreshold * ReturnFinishThreshold)
            {
                positionReached = true;
            }
        }

        if (Type.HasFlag(ShakeType.Rotation))
        {
            _currentRotationOffset = _currentRotationOffset.Lerp(Vector3.Zero, ReturnSpeed * delta);
            if (_currentRotationOffset.LengthSquared() < ReturnFinishThreshold * ReturnFinishThreshold)
            {
                rotationReached = true;
            }
        }
        
        if (positionReached && rotationReached)
        {
            _currentState = ShakerState.Inactive;
            _currentPositionOffset = Vector3.Zero;
            _currentRotationOffset = Vector3.Zero;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Инициализирует генератор шума.
    /// </summary>
    private void InitializeNoise()
    {
        _noise.Seed = (int)GD.Randi();
    }

    /// <summary>
    /// Находит и проверяет целевой узел для трансформаций.
    /// </summary>
    private void InitializeTarget()
    {
        _target ??= GetParent() as Node3D;
#if DEBUG
        if (_target == null)
        {
            GD.PushError($"Shaker3D на узле '{Name}' не смог найти целевой Node3D. Убедитесь, что он является дочерним для Node3D.");
        }
#endif
    }
    
    /// <summary>
    /// Кэширует базовые (до тряски) трансформации целевого узла.
    /// </summary>
    private void CacheBaseTransformations()
    {
        if (_target != null)
        {
            _basePosition = _target.Position;
            _baseRotation = _target.Rotation;
        }
    }

    /// <summary>
    /// Вычисляет текущие смещения на основе шума и сохраняет их в поля класса.
    /// </summary>
    private void CalculateCurrentOffsets()
    {
        float currentStrength = CalculateCurrentStrength();
        float time = _elapsedTime * Frequency;
        
        if (Type.HasFlag(ShakeType.Position))
        {
            _currentPositionOffset.X = _noise.GetNoise2D(time, NoiseOffsetXPos) * currentStrength;
            _currentPositionOffset.Y = _noise.GetNoise2D(time, NoiseOffsetYPos) * currentStrength;
            _currentPositionOffset.Z = _noise.GetNoise2D(time, NoiseOffsetZPos) * currentStrength;
        }

        if (Type.HasFlag(ShakeType.Rotation))
        {
            float rotationStrength = currentStrength * Mathf.DegToRad(RotationStrengthMultiplier);
            _currentRotationOffset.X = _noise.GetNoise2D(time, NoiseOffsetXRot) * rotationStrength;
            _currentRotationOffset.Y = _noise.GetNoise2D(time, NoiseOffsetYRot) * rotationStrength;
            _currentRotationOffset.Z = _noise.GetNoise2D(time, NoiseOffsetZRot) * rotationStrength;
        }
    }

    /// <summary>
    /// Вычисляет текущую интенсивность тряски с плавным затуханием.
    /// </summary>
    private float CalculateCurrentStrength()
    {
        float progress = Mathf.Clamp(_elapsedTime / _duration, 0.0f, 1.0f);
        float decay = 1.0f - progress;
        return _strength * (decay * decay); // Квадратичное затухание (ease-out)
    }
    
    /// <summary>
    /// Обновляет прошедшее с начала тряски время.
    /// </summary>
    private void UpdateElapsedTime()
    {
        _elapsedTime = (float)(Time.GetTicksMsec() / 1000.0 - _startTime);
    }
    
    /// <summary>
    /// Осуществляет переход в состояние возврата или неактивное состояние.
    /// </summary>
    private void TransitionToReturnOrInactive()
    {
        if (_useSmoothReturn)
        {
            _currentState = ShakerState.Returning;
        }
        else
        {
            _currentState = ShakerState.Inactive;
            _currentPositionOffset = Vector3.Zero;
            _currentRotationOffset = Vector3.Zero;
        }
    }

    #endregion
}
#nullable disable