using Godot;

namespace Game;

/// <summary>
/// Класс для хранения глобальных констант.
/// Является синглтоном (Autoload).
/// </summary>
public static class Constants
{
    #region Input Action Names

    public const string ActionMoveLeft = "move_left";
    public const string ActionMoveRight = "move_right";
    public const string ActionMoveForward = "move_forward";
    public const string ActionMoveBackward = "move_backward";
    public const string ActionJump = "jump";
    public const string ActionInteract = "interact";
    public const string ActionFreecamToggle = "freecam";
    public const string ActionFreecamUp = "freecam_up";
    public const string ActionFreecamDown = "freecam_down";
    public const string ActionFreecamBoost = "freecam_boost";
    public const string ActionFreecamSlow = "freecam_slow";
    public const string ActionZoomIn = "zoom_in";
    public const string ActionZoomOut = "zoom_out";

    #endregion

    #region Shader Parameter Names

    public static readonly StringName SP_TurretReticle_Spread = "spread";
    public static readonly StringName SP_TurretReticle_DiamondRot = "diamond_rotation";
    public static readonly StringName SP_TurretReticle_Yaw = "yaw_degrees";
    public static readonly StringName SP_TurretReticle_Pitch = "pitch_degrees";
    public static readonly StringName SP_TurretReticle_TurretState = "turret_state";
    public static readonly StringName SP_TurretReticle_StateTime = "state_time";
    public static readonly StringName SP_TurretReticle_ReticleGap = "reticle_gap";
    public static readonly StringName SP_TurretReticle_DiamondSize = "diamond_base_size";
    public static readonly StringName SP_TurretReticle_EdgeMargin = "edge_margin";
    public static readonly StringName SP_TurretReticle_PPD = "pixels_per_degree";
    public static readonly StringName SP_TurretReticle_MinorInterval = "minor_interval";
    public static readonly StringName SP_TurretReticle_ZoomLevel = "zoom_level";
    public static readonly StringName SP_TurretReticle_Convergence = "convergence_intensity";
    public static readonly StringName SP_TurretReticle_ShotFlash = "shot_flash";
    public static readonly StringName SP_TurretReticle_ImpactRing = "impact_ring";
    public static readonly StringName SP_TurretReticle_Frost = "frost_level";

    public static readonly StringName SP_GridOverlay_Intensity = "intensity";
    public static readonly StringName SP_GridOverlay_GridColorLarge = "grid_color_large";
    public static readonly StringName SP_GridOverlay_GridColorSmall = "grid_color_small";
    public static readonly StringName SP_GridOverlay_RectSize = "rect_size";
    public static readonly StringName SP_GridOverlay_AlertLevel = "alert_level";
    public static readonly StringName SP_GridOverlay_BorderColor = "border_color";
    public static readonly StringName SP_GridOverlay_CornerColor = "corner_accent_color";

    public static readonly StringName SP_PixelationOverlay_Intensity = "pixelation_intensity";
    public static readonly StringName SP_PixelationOverlay_ZoomLevel = "zoom_level";
    public static readonly StringName SP_PixelationOverlay_MinPixelSize = "min_pixel_size";
    public static readonly StringName SP_PixelationOverlay_MaxPixelSize = "max_pixel_size";

    public static readonly StringName SP_GlitchOverlay_Intensity = "glitch_intensity";
    public static readonly StringName SP_GlitchOverlay_Tint = "color_tint";
    public static readonly StringName SP_GlitchOverlay_Time = "time";

    #endregion

    #region Animation Names

    public const string AnimPlayer_HUD_Boot = "Boot";
    public const string AnimPlayer_HUD_Shutdown = "Shutdown";

    #endregion
}