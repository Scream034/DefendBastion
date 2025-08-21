using Godot;

namespace Game;

public partial class Constants : Node
{
    public static SceneTree Tree { get; private set; }
    public static Window Root { get; private set; }
    public static float DefaultGravity { get; private set; }
    public static Vector3 DefaultGravityVector { get; private set; }

    public override void _EnterTree()
    {
        Tree = GetTree();
        Root = Tree.Root;
        DefaultGravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        DefaultGravityVector = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3();
    }
}