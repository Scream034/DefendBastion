using Godot;

namespace Game.Singletons;

/// <summary>
/// Класс для хранения глобальных констант.
/// Является синглтоном (Autoload).
/// </summary>
public sealed partial class Constants : Node
{
    public static SceneTree Tree { get; private set; }
    public static Window Root { get; private set; }

    public override void _EnterTree()
    {
        base._EnterTree();
        Tree = GetTree();
        Root = Tree.Root;
    }
}