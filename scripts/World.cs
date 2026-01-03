using System.Threading.Tasks;
using Godot;

namespace Game;

public partial class World : Node3D
{
    public static World Instance { get; private set; }
    public static float DefaultGravity { get; private set; }
    public static Vector3 DefaultGravityVector { get; private set; }
    public static PhysicsDirectSpaceState3D DirectSpaceState { get; private set; }
    public static World3D Real { get; private set; }

    [Signal]
    public delegate void NavigationReadyEventHandler();

    [Export] private AudioStreamPlayer _audioAmbient;
    [Export] private AudioStreamPlayer _audioVictory;
    [Export] private AudioStreamPlayer _audioDefeat;

    private Rid _navigationMapRid; // Сохраняем RID карты здесь
    private readonly static PhysicsRayQueryParameters3D _rayQueryParams = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1, []);

    public bool IsNavigationReady { get; private set; } = false;

    public override void _EnterTree()
    {
        // Установка синглтона
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            GD.PushWarning("GameManager instance already exists. Overwriting.");
            Instance = this;
        }

        DefaultGravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        DefaultGravityVector = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3();
    }

    public override async void _Ready()
    {
        await UpdateStateAsync(GetWorld3D());

        // Получаем и сохраняем RID карты
        _navigationMapRid = Real.NavigationMap;

        // Подписываемся, используя ИМЯ метода, а не лямбду
        NavigationServer3D.Singleton.MapChanged += OnMapChanged;

        while (!IsNavigationReady)
        {
            await Task.Delay(25);
        }

        AudioServer.SetBusVolumeDb(0, -16f);
    }

    public static async Task<PhysicsDirectSpaceState3D> GetRealDirectSpaceStateAsync()
    {
        await Instance.GetTree().ToSignal(Instance.GetTree(), SceneTree.SignalName.PhysicsFrame);
        return Real.DirectSpaceState;
    }

    /// <summary>
    /// Должен ожидать перед этим PhysicsFrame для корректной работы.
    /// </summary>
    /// <param name="world"></param>
    public static async Task UpdateStateAsync(World3D world)
    {
        await Instance.GetTree().ToSignal(Instance.GetTree(), SceneTree.SignalName.PhysicsFrame);

        DirectSpaceState = world.DirectSpaceState;
        Real = world;
    }

    public static Godot.Collections.Dictionary IntersectRay(Vector3 from, Vector3 to, uint collisionMask = 1, Godot.Collections.Array<Rid> exclude = default)
    {
        _rayQueryParams.From = from;
        _rayQueryParams.To = to;
        _rayQueryParams.CollisionMask = collisionMask;
        _rayQueryParams.Exclude = exclude;

        return DirectSpaceState.IntersectRay(_rayQueryParams);
    }

    /// <summary>
    /// Этот метод будет вызываться КАЖДЫЙ раз, когда ЛЮБАЯ навигационная карта меняется.
    /// </summary>
    private void OnMapChanged(Rid mapRid)
    {
        // Мы проверяем, что изменилась именно та карта, которая нас интересует.
        if (mapRid == _navigationMapRid)
        {
            OnNavigationMapReady();
        }
    }

    private void OnNavigationMapReady()
    {
        if (IsNavigationReady) return; // Защита от повторного вызова

        // Теперь отписка работает, потому что мы используем ту же самую ссылку на метод
        NavigationServer3D.Singleton.MapChanged -= OnMapChanged;

        IsNavigationReady = true;
        GD.Print("-> Navigation map is ready! Emitting NavigationReady signal.");
        EmitSignal(SignalName.NavigationReady);
    }
}