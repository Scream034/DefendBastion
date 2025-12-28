#nullable enable

using Game.Player;
using Godot;
using System;

namespace Game.UI;

public partial class PlayerHUD : Control
{
    [ExportGroup("Components")]
    [Export] private Label _compassLabel = null!;

    [ExportGroup("Common")]
    [Export] private Label _interactionLabel = null!;
    [Export] private AnimationPlayer _animPlayer = null!;

    private float _targetIntegrity = 100f;
    private Color _baseColor = new(0, 1, 1, 0.8f);
    private Color _warningColor = new(1, 0.2f, 0.2f, 1.0f);

    public override void _Ready()
    {
        _interactionLabel.Visible = false;
        _animPlayer.Play("Boot");
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateCompass();
    }

    #region Public API

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