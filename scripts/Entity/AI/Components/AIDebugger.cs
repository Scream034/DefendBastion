using Godot;
using System.Text;
using System.Linq;
using Game.Entity.AI.Components;

namespace Game.Entity.AI.Debug
{
    public partial class AIDebugger : Node
    {
        [ExportGroup("Targeting")]
        [Export] public AIEntity Context { get; set; }

        [ExportGroup("Settings")]
        [Export] public bool IsEnabled { get; set; } = true;
        [Export] public bool ShowVisionCone { get; set; } = true;
        [Export] public bool ShowNavigation { get; set; } = true;

        // Цвета
        private readonly Color _visionColor = new Color(0, 1, 1, 0.3f);
        private readonly Color _pathColor = Colors.Green;
        private readonly Color _targetLockedColor = Colors.Red;
        private readonly Color _targetNoLosColor = new Color(1, 0, 0, 0.3f);

        // Компоненты
        private Label3D _statusLabel;
        private MeshInstance3D _gizmoMeshInstance;
        private ImmediateMesh _immediateMesh;
        private Material _gizmoMaterial;

        // --- Кешированные данные (для передачи из Physics в Process) ---
        private bool _hasLineOfSightToTarget;
        private Vector3 _currentLookTarget;
        private Vector3 _nextPathPoint;
        private bool _isPathActive;
        private Vector3? _targetPosition;

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

            SetupLabel();
            SetupGizmos();
        }

        // 1. ФАЗА ФИЗИКИ: Безопасно делаем RayCast и читаем данные
        public override void _PhysicsProcess(double delta)
        {
            if (!IsEnabled || !Context.IsInsideTree()) return;

            // Кешируем данные навигации
            var nav = Context.MovementController.NavigationAgent;
            _isPathActive = !nav.IsNavigationFinished();
            if (_isPathActive)
            {
                _nextPathPoint = nav.GetNextPathPosition();
            }

            // Кешируем данные взгляда
            _currentLookTarget = Context.LookController.RawTargetPosition;

            // Кешируем данные цели и делаем RayCast
            var target = Context.TargetingSystem.CurrentTarget;
            if (IsInstanceValid(target))
            {
                _targetPosition = target.GlobalPosition;
                // БЕЗОПАСНЫЙ ВЫЗОВ РЕЙКАСТА (т.к. мы в PhysicsProcess)
                _hasLineOfSightToTarget = Context.GetVisibleTargetPoint(target).HasValue;
            }
            else
            {
                _targetPosition = null;
                _hasLineOfSightToTarget = false;
            }
        }

        // 2. ФАЗА ОТРИСОВКИ: Обновляем графику и текст (интерполяция)
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

            // Рисуем линии
            _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _gizmoMaterial);

            if (ShowVisionCone) DrawVision();
            if (ShowNavigation) DrawNavigation();
            DrawCombat();

            _immediateMesh.SurfaceEnd();
        }

        // --- Логика Отрисовки ---

        private void UpdateLabelText()
        {
            var sb = new StringBuilder();

            // Раскрашиваем здоровье: Зеленый -> Желтый -> Красный
            sb.AppendLine($"{Context.Name} | HP: {(int)Context.Health}");

            string stateName = Context.Squad?.CurrentState?.ToString().Split('.').LastOrDefault() ?? "No Squad";
            sb.AppendLine($"State: {stateName}");

            if (_targetPosition.HasValue)
            {
                float dist = Context.GlobalPosition.DistanceTo(_targetPosition.Value);
                sb.AppendLine($"Target: {dist:F1}m (LoS: {(_hasLineOfSightToTarget ? "YES" : "NO")})");
            }
            else
            {
                sb.AppendLine("Target: None");
            }

            _statusLabel.Text = sb.ToString();
        }

        private void UpdateLabelOrientation()
        {
            var camera = GetViewport().GetCamera3D();
            if (camera == null) return;

            // Чтобы текст всегда смотрел на камеру и был читаем
            _statusLabel.GlobalPosition = Context.GlobalPosition + Vector3.Up * 2.3f;

            // Вариант 1: Billboard (уже включен в Label3D), но иногда нужен ручной LookAt для корректного Z-смещения
            // Вариант 2 (Надежный):
            _statusLabel.LookAt(camera.GlobalPosition, Vector3.Up);
            _statusLabel.RotateObjectLocal(Vector3.Up, Mathf.Pi); // Разворот на 180
        }

        private void DrawVision()
        {
            Vector3 eyes = Context.EyesPosition?.GlobalPosition ?? (Context.GlobalPosition + Vector3.Up * 1.5f);
            DrawLine(eyes, _currentLookTarget, _visionColor);
        }

        private void DrawNavigation()
        {
            if (_isPathActive)
            {
                DrawLine(Context.GlobalPosition + Vector3.Up * 0.1f, _nextPathPoint, _pathColor);
                DrawWireSphere(_nextPathPoint, 0.2f, _pathColor);
            }
        }

        private void DrawCombat()
        {
            if (_targetPosition.HasValue)
            {
                Vector3 start = Context.EyesPosition?.GlobalPosition ?? (Context.GlobalPosition + Vector3.Up * 1.5f);
                Vector3 end = _targetPosition.Value + Vector3.Up; // Целимся примерно в центр

                // Используем результат, полученный в PhysicsProcess
                Color color = _hasLineOfSightToTarget ? _targetLockedColor : _targetNoLosColor;

                DrawLine(start, end, color);
                DrawWireSphere(_targetPosition.Value, 0.5f, color);
            }
        }

        // --- Хелперы Графики ---

        private void DrawLine(Vector3 from, Vector3 to, Color color)
        {
            _immediateMesh.SurfaceSetColor(color);
            _immediateMesh.SurfaceAddVertex(from);
            _immediateMesh.SurfaceAddVertex(to);
        }

        private void DrawWireSphere(Vector3 center, float radius, Color color, int segments = 8)
        {
            _immediateMesh.SurfaceSetColor(color);
            float step = Mathf.Tau / segments;

            // Рисуем крест-накрест окружности для имитации сферы
            for (int i = 0; i < segments; i++)
            {
                float a1 = i * step;
                float a2 = (i + 1) * step;

                // Горизонталь
                Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Vector3 p2 = center + new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * radius;
                _immediateMesh.SurfaceAddVertex(p1); _immediateMesh.SurfaceAddVertex(p2);

                // Вертикаль
                Vector3 p3 = center + new Vector3(0, Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                Vector3 p4 = center + new Vector3(0, Mathf.Cos(a2), Mathf.Sin(a2)) * radius;
                _immediateMesh.SurfaceAddVertex(p3); _immediateMesh.SurfaceAddVertex(p4);
            }
        }

        // --- Инициализация ---

        private void SetupLabel()
        {
            _statusLabel = new Label3D
            {
                Name = "DebugLabel",
                Shaded = false,
                NoDepthTest = true,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = Vector3.Up * 2.3f,
                PixelSize = 0.025f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, // Четкие края букв
                FontSize = 16,
                OutlineSize = 0,
                OutlineRenderPriority = 0,
                Modulate = Colors.White,
                OutlineModulate = Colors.Black,
                AlphaCut = Label3D.AlphaCutMode.Discard, // Четкие края букв
                DoubleSided = true,
                ProcessPriority = int.MaxValue, // Чтобы рисовалось в самом конце
                ProcessPhysicsPriority = int.MaxValue,
                VisibilityRangeEnd = 200f
            };

            // Безопасное добавление
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
                    NoDepthTest = true,
                    AlbedoColor = Colors.White
                },
                ProcessPriority = int.MinValue + 1, // Чтобы рисовалось в самом конце
                ProcessPhysicsPriority = int.MinValue + 1
            };

            CallDeferred(Node.MethodName.AddChild, _gizmoMeshInstance);
        }
    }
}