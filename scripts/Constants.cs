using Godot;

namespace Game;

public sealed partial class Constants : Node
{
    public static SceneTree Tree { get; private set; }
    public static Window Root { get; private set; }
    public static float DefaultGravity { get; private set; }
    public static Vector3 DefaultGravityVector { get; private set; }
    public static PhysicsDirectSpaceState3D DirectSpaceState { get; private set; }

    public override void _EnterTree()
    {
        base._EnterTree();
        Tree = GetTree();
        Root = Tree.Root;
        DefaultGravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        DefaultGravityVector = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3();
    }

    /// <summary>
    /// Должен ожидать перед этим PhysicsFrame для корректной работы.
    /// </summary>
    /// <param name="world"></param>
    public static void UpdateWorldConstants(World3D world)
    {
        DirectSpaceState = world.DirectSpaceState;
    }
}