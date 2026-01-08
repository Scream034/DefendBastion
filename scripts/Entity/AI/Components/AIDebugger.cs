using Godot;
using System.Text;
using System.Linq;
using System.Reflection;
using Game.Entity.AI.States.Squad;

namespace Game.Entity.AI.Debug;

/// <summary>
/// Компонент для визуальной отладки поведения ИИ.
/// Рисует линии видимости, пути навигации, состояние цели и логические зоны.
/// </summary>
public partial class AIDebugger : Node
{
    [ExportGroup("Targeting")]
    [Export] public AIEntity Context { get; set; }

    [ExportGroup("Settings")]
    [Export] public bool IsEnabled { get; set; } = true;
    [Export] public bool ShowVisionCone { get; set; } = true;
    [Export] public bool ShowNavigation { get; set; } = true;
    [Export] public bool ShowCombatGizmos { get; set; } = true;

    // --- ЦВЕТОВАЯ ПАЛИТРА (Улучшенная) ---
    // Второстепенное - темное и прозрачное
    private readonly Color _visionColor = new(0, 0.3f, 0.3f, 0.05f); // Очень тусклый циан
    private readonly Color _pathColor = new(0, 0.4f, 0, 0.3f);       // Тусклый зеленый
    private readonly Color _leashColor = new(1, 1, 0, 0.15f);        // Тусклый желтый круг

    // Важное - яркое
    private readonly Color _targetLockedColor = new(1, 0, 0, 1.0f);   // Ярко-красный (Атака!)
    private readonly Color _targetLostColor = new(0.5f, 0, 0, 0.1f);  // Тусклый красный (Вижу, но игнорирую/Вне зоны)
    private readonly Color _lastKnownPosColor = new(1, 0.5f, 0, 0.8f);// Оранжевый (Куда бегу)

    // Компоненты
    private Label3D _statusLabel;
    private MeshInstance3D _gizmoMeshInstance;
    private ImmediateMesh _immediateMesh;
    private Material _gizmoMaterial;

    // --- Кешированные данные (Physics -> Process) ---
    private bool _hasLineOfSight;
    private bool _isInSensorRange; // Новое: проверка зоны Area3D
    private Vector3 _currentLookTarget;
    private Vector3 _nextPathPoint;
    private bool _isPathActive;
    private Vector3? _targetPosition;

    // Данные Squad
    private Vector3? _lastKnownTargetPos;
    private Vector3? _squadCenter;
    private float _maxPursuitDist;
    private double _pursuitLostSightTimer = 0;
    private float _maxLoseSightTime = 0;
    private bool _isInPursuit = false;

    // Reflection
    private FieldInfo _pursuitTimerField;

    public override void _Ready()
    {
        if (Context == null) Context = GetParentOrNull<AIEntity>();
        if (Context == null)
        {
            GD.PushError($"AIDebugger {Name}: Context is null!");
            SetProcess(false);
            SetPhysicsProcess(false);
            return;
        }

        _pursuitTimerField = typeof(PursuitState).GetField("_pursuitTimer", BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? typeof(PursuitState).GetField("_timeWithoutSight", BindingFlags.NonPublic | BindingFlags.Instance);

        SetupLabel();
        SetupGizmos();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsEnabled || !Context.IsInsideTree()) return;

        // 1. Навигация
        var nav = Context.MovementController.NavigationAgent;
        _isPathActive = !nav.IsNavigationFinished();
        if (_isPathActive)
            _nextPathPoint = nav.GetNextPathPosition();

        // 2. Взгляд
        _currentLookTarget = Context.LookController.FinalLookPosition;

        // 3. Цель и Сенсоры
        var target = Context.TargetingSystem.CurrentTarget;
        if (IsInstanceValid(target))
        {
            _targetPosition = target.GlobalPosition;
            // RayCast (Физическая видимость)
            _hasLineOfSight = Context.GetVisibleTargetPoint(target).HasValue;
            // Area3D (Логическая зона обнаружения)
            _isInSensorRange = Context.TargetingSystem.IsTargetInSensorRange(target);
        }
        else
        {
            _targetPosition = null;
            _hasLineOfSight = false;
            _isInSensorRange = false;
        }

        // 4. Squad данные
        if (Context.Squad != null)
        {
            _lastKnownTargetPos = Context.Squad.LastKnownTargetPosition;
            _squadCenter = Context.Squad.GetSquadCenter();
            _maxPursuitDist = Context.Squad.MaxPursuitDistance; // Круг предела погони

            if (Context.Squad.CurrentState is PursuitState pursuitState)
            {
                _isInPursuit = true;
                _maxLoseSightTime = Context.Squad.PursuitGiveUpTime;
                if (_pursuitTimerField != null)
                    _pursuitLostSightTimer = (double)_pursuitTimerField.GetValue(pursuitState);
            }
            else
            {
                _isInPursuit = false;
                _pursuitLostSightTimer = 0;
            }
        }
    }

    public override void _Process(double delta)
    {
        _immediateMesh.ClearSurfaces();

        if (!IsEnabled || !Context.IsInsideTree())
        {
            _statusLabel.Visible = false;
            return;
        }

        _statusLabel.Visible = true;
        UpdateLabelText();
        UpdateLabelOrientation();

        _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _gizmoMaterial);

        if (ShowVisionCone) DrawVision();
        if (ShowNavigation) DrawNavigation();
        if (ShowCombatGizmos) DrawCombat();

        _immediateMesh.SurfaceEnd();
    }

    private void UpdateLabelText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Context.Name} | HP: {(int)Context.Health}");

        string stateName = Context.Squad?.CurrentState?.ToString().Split('.').LastOrDefault() ?? "No Squad";
        sb.AppendLine($"State: {stateName}");

        if (_targetPosition.HasValue)
        {
            float dist = Context.GlobalPosition.DistanceTo(_targetPosition.Value);
            // Показываем статус и RayCast и Сенсора
            string vis = _hasLineOfSight ? "Vis" : "---";
            string sen = _isInSensorRange ? "Sen" : "---";
            sb.AppendLine($"Target: {dist:F1}m [{vis} | {sen}]");
        }
        else
        {
            sb.AppendLine("Target: None");
        }

        if (_isInPursuit)
        {
            if (_pursuitLostSightTimer > 0)
            {
                float remaining = _maxLoseSightTime - (float)_pursuitLostSightTimer;
                string colorHex = remaining < 5.0f ? "ff4444" : "ffffff";
                sb.AppendLine($"GIVE UP IN: {remaining:F1}s");
            }
            else
            {
                sb.AppendLine("CHASING ACTIVE");
            }
        }

        _statusLabel.Text = sb.ToString();
        // Делаем текст красным, если потеряли сенсорную связь, но еще преследуем
        _statusLabel.Modulate = (_isInPursuit && !_isInSensorRange) ? Colors.Orange : Colors.White;
    }

    private void UpdateLabelOrientation()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null) return;
        _statusLabel.GlobalPosition = Context.GlobalPosition + Vector3.Up * 2.4f;
        _statusLabel.LookAt(camera.GlobalPosition, Vector3.Up);
        _statusLabel.RotateObjectLocal(Vector3.Up, Mathf.Pi);
    }

    // --- DRAWING LOGIC ---

    private void DrawVision()
    {
        Vector3 eyes = Context.EyesPosition?.GlobalPosition ?? (Context.GlobalPosition + Vector3.Up * 1.5f);
        DrawLine(eyes, _currentLookTarget, _visionColor);
    }

    private void DrawNavigation()
    {
        if (_isPathActive)
        {
            Vector3 start = Context.GlobalPosition + Vector3.Up * 0.1f;
            DrawLine(start, _nextPathPoint, _pathColor);
            // Маленький крестик на следующей точке пути
            DrawWireCross(_nextPathPoint, 0.2f, _pathColor);
        }
    }

    private void DrawCombat()
    {
        Vector3 eyes = Context.EyesPosition?.GlobalPosition ?? (Context.GlobalPosition + Vector3.Up * 1.5f);

        // 1. ЛИНИЯ К ЦЕЛИ (Исправленная логика)
        if (_targetPosition.HasValue)
        {
            Vector3 end = _targetPosition.Value + Vector3.Up;

            // КРАСНАЯ ЛИНИЯ (LOCKED): Только если видим И мы в зоне.
            bool isLocked = _hasLineOfSight && _isInSensorRange;

            if (isLocked)
            {
                // Рисуем "жирную" линию (несколько линий со смещением)
                DrawThickLine(eyes, end, _targetLockedColor, 0.02f);
                DrawWireSphere(_targetPosition.Value, 0.5f, _targetLockedColor);
            }
            else
            {
                // ТУСКЛАЯ ЛИНИЯ: Если видим, но игнорируем (вне зоны), или не видим
                DrawLine(eyes, end, _targetLostColor);
            }
        }

        // 2. ПОСЛЕДНЯЯ ИЗВЕСТНАЯ ПОЗИЦИЯ (Оранжевая сфера)
        // Рисуем, если мы в бою/погоне и эта точка отличается от текущей позиции ИИ
        if (_lastKnownTargetPos.HasValue && Context.Squad != null && Context.Squad.IsInCombat)
        {
            float dist = Context.GlobalPosition.DistanceTo(_lastKnownTargetPos.Value);
            if (dist > 1.0f)
            {
                DrawWireSphere(_lastKnownTargetPos.Value, 0.5f, _lastKnownPosColor, 12);
                // Линия от ног к точке интереса
                DrawLine(Context.GlobalPosition, _lastKnownTargetPos.Value, _lastKnownPosColor);
            }
        }

        // 3. КРУГ ПРЕДЕЛА ПРЕСЛЕДОВАНИЯ (MaxPursuitDistance)
        // Рисуем вокруг центра отряда
        if (_squadCenter.HasValue && _isInPursuit)
        {
            DrawWireCircle(_squadCenter.Value, _maxPursuitDist, _leashColor, 32);
        }
    }

    // --- GRAPHICS HELPERS ---

    private void DrawLine(Vector3 from, Vector3 to, Color color)
    {
        _immediateMesh.SurfaceSetColor(color);
        _immediateMesh.SurfaceAddVertex(from);
        _immediateMesh.SurfaceAddVertex(to);
    }

    // Имитация толстой линии
    private void DrawThickLine(Vector3 from, Vector3 to, Color color, float width)
    {
        Vector3 offsetUp = Vector3.Up * width;
        Vector3 offsetRight = from.DirectionTo(to).Cross(Vector3.Up) * width;

        DrawLine(from + offsetUp, to + offsetUp, color);
        DrawLine(from - offsetUp, to - offsetUp, color);
        DrawLine(from + offsetRight, to + offsetRight, color);
        DrawLine(from - offsetRight, to - offsetRight, color);
        DrawLine(from, to, color); // Центр
    }

    private void DrawWireCross(Vector3 center, float size, Color color)
    {
        DrawLine(center - Vector3.Right * size, center + Vector3.Right * size, color);
        DrawLine(center - Vector3.Forward * size, center + Vector3.Forward * size, color);
    }

    private void DrawWireSphere(Vector3 center, float radius, Color color, int segments = 8)
    {
        _immediateMesh.SurfaceSetColor(color);
        float step = Mathf.Tau / segments;

        for (int i = 0; i < segments; i++)
        {
            float a1 = i * step;
            float a2 = (i + 1) * step;

            // Экватор
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
            Vector3 p2 = center + new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * radius;
            _immediateMesh.SurfaceAddVertex(p1); _immediateMesh.SurfaceAddVertex(p2);

            // Меридиан 1
            Vector3 p3 = center + new Vector3(0, Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
            Vector3 p4 = center + new Vector3(0, Mathf.Cos(a2), Mathf.Sin(a2)) * radius;
            _immediateMesh.SurfaceAddVertex(p3); _immediateMesh.SurfaceAddVertex(p4);

            // Меридиан 2 (перпендикулярный)
            Vector3 p5 = center + new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0);
            Vector3 p6 = center + new Vector3(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius, 0);
            _immediateMesh.SurfaceAddVertex(p5); _immediateMesh.SurfaceAddVertex(p6);
        }
    }

    private void DrawWireCircle(Vector3 center, float radius, Color color, int segments = 32)
    {
        _immediateMesh.SurfaceSetColor(color);
        float step = Mathf.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float a1 = i * step;
            float a2 = (i + 1) * step;

            // Рисуем чуть выше земли (0.1f), чтобы не клипалось
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0.1f, Mathf.Sin(a1)) * radius;
            Vector3 p2 = center + new Vector3(Mathf.Cos(a2), 0.1f, Mathf.Sin(a2)) * radius;

            _immediateMesh.SurfaceAddVertex(p1);
            _immediateMesh.SurfaceAddVertex(p2);
        }
    }

    // --- SETUP ---

    private void SetupLabel()
    {
        _statusLabel = new Label3D
        {
            Name = "DebugLabel",
            Shaded = false,
            NoDepthTest = true, // Всегда поверх
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = Vector3.Up * 2.3f,
            PixelSize = 0.025f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            FontSize = 16,
            OutlineSize = 2,
            OutlineRenderPriority = 0,
            Modulate = Colors.White,
            OutlineModulate = Colors.Black,
            AlphaCut = Label3D.AlphaCutMode.Discard,
            DoubleSided = true,
            ProcessPriority = int.MaxValue
        };
        CallDeferred(Node.MethodName.AddChild, _statusLabel);
    }

    private void SetupGizmos()
    {
        _immediateMesh = new ImmediateMesh();
        _gizmoMeshInstance = new MeshInstance3D
        {
            Name = "DebugGizmos",
            Mesh = _immediateMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = true, // Всегда поверх геометрии
                AlbedoColor = Colors.White
            }
        };
        CallDeferred(Node.MethodName.AddChild, _gizmoMeshInstance);
    }
}