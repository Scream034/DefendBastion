using Godot;

namespace Game;

public sealed partial class UI : CanvasLayer
{
    public static UI Instance { get; private set; }

    [ExportGroup("Nodes")]
    [Export]
    public Control Crosshair { get; private set; }

    [Export]
    public ProgressBar BossProgressBar { get; private set; }

    [Export]
    public Label GameStateLabel { get; private set; }

    [Export]
    public Label InteractionLabel { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        InteractionLabel.Visible = false;
    }

    public void UpdateProgress(in float progress)
    {
        BossProgressBar.Value = progress;
    }

    public void ShowEndState(string state)
    {
        BossProgressBar.Value = 0;
        Crosshair.Visible = true;
        GameStateLabel.Text = state;
        GameStateLabel.Visible = true;
    }

    public void SetInteractionText(string text)
    {
        InteractionLabel.Text = text;
        InteractionLabel.Visible = true;
    }

    public void HideInteractionText()
    {
        InteractionLabel.Visible = false;
    }
}
