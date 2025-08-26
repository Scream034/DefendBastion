using System.Threading.Tasks;
using Game.Singletons;
using Godot;

namespace Game;

public partial class GameManager : Node3D
{
    public static GameManager Instance { get; private set; }

    [Signal]
    public delegate void NavigationReadyEventHandler();

    [Export] private AudioStreamPlayer _audioAmbient;
    [Export] private AudioStreamPlayer _audioVictory;
    [Export] private AudioStreamPlayer _audioDefeat;

    private Rid _navigationMapRid; // Сохраняем RID карты здесь

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
    }

    public override async void _Ready()
    {
        // Получаем и сохраняем RID карты
        _navigationMapRid = GetWorld3D().NavigationMap;

        // Подписываемся, используя ИМЯ метода, а не лямбду
        NavigationServer3D.Singleton.MapChanged += OnMapChanged;

        while (!IsNavigationReady)
        {
            await Task.Delay(25);
        }

        AudioServer.SetBusVolumeDb(0, -16f);
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

        Constants.UpdateWorldConstants(GetWorld3D());

        IsNavigationReady = true;
        GD.Print("-> Navigation map is ready! Emitting NavigationReady signal.");
        EmitSignal(SignalName.NavigationReady);
    }
}