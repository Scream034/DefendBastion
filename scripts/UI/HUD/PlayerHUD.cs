#nullable enable

using Game.Player;
using Godot;
using System;

namespace Game.UI.HUD;

public partial class PlayerHUD : Control
{
    [ExportGroup("Components")]
    [Export] private Label _compassLabel = null!;

    [ExportGroup("Common")]
    [Export] private Label _interactionLabel = null!;
    [Export] private AnimationPlayer _animPlayer = null!;

    public override void _Ready()
    {
        _interactionLabel.Visible = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateCompass();
    }

    #region Public API

    public void ShowHUD()
    {
        Visible = true;
        _animPlayer.Play("Boot");
        SharedHUD.SetLoggerPreset(LoggerPreset.Full);
        SharedHUD.SetLoggerVisible(true);

        SetProcess(true);
        SetPhysicsProcess(true);
    }

    public void HideHUD()
    {
        // _animPlayer.Play("Shutdown");
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    public void SetInteraction(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _interactionLabel.Visible = false;
        }
        else
        {
            _interactionLabel.Text = $"> {text.ToUpper()} <";
            _interactionLabel.Visible = true;
        }
    }

    #endregion

    private void UpdateCompass()
    {
        float yaw = Mathf.RadToDeg(LocalPlayer.Instance.Head.GlobalRotation.Y);
        float degrees = (360 + (int)Math.Round(yaw)) % 360;
        string[] directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        int index = (int)Math.Round(degrees / 45) % 8;
        _compassLabel.Text = $"{directions[index]} | {degrees}°";
    }
}