#nullable enable

using Godot;
using Game.Turrets;
using Game.UI.Components;

namespace Game.UI.HUD;

public partial class TurretHUD : Control
{
    [ExportGroup("Components")]
    [Export] private AnimationPlayer _animPlayer = null!;
    [Export] private TurretReticle _reticle = null!;
    [Export] private ColorRect _gridOverlay = null!;

    [ExportGroup("Info Labels")]
    [Export] private Label _distanceLabel = null!;
    [Export] private Label _ammoLabel = null!;
    [Export] private Label _statusLabel = null!;
    [Export] private ProgressBar _ammoBar = null!;

    [ExportGroup("Grid Settings")]
    [Export] private float _gridFadeSpeed = 5f;
    [Export] private float _gridMinIntensity = 0.3f;
    [Export] private float _gridMaxIntensity = 1.0f;

    [ExportGroup("Status Animation")]
    [Export] private float _statusFadeDuration = 0.15f;
    [Export] private float _statusTypeSpeed = 40f;

    private ShaderMaterial? _gridMaterial;
    private float _currentGridIntensity = 0f;
    private float _targetGridIntensity = 0f;

    private PlayerControllableTurret? _turret;

    private Tween? _statusTween;
    private string _targetStatusText = "";
    private int _displayedCharCount = 0;
    private int _lastAmmoCount = -1;
    private ShootingTurret.TurretState _lastState;

    public override void _Ready()
    {
        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);

        if (_gridOverlay != null)
            _gridMaterial = _gridOverlay.Material as ShaderMaterial;
    }

    public void ShowHUD(PlayerControllableTurret turret, TurretCameraController camera)
    {
        _turret = turret;
        Visible = true;

        // Переключаем логгер на позицию для турели
        SharedHUD.SetLoggerPreset(LoggerPreset.FullLessLines);
        SharedHUD.SetLoggerVisible(true);

        RobotBus.Net("INIT: NEURAL_LINK_ESTABLISHED");

        if (_reticle != null)
        {
            _reticle.Initialize(turret, camera);
            _reticle.OnAimingIntensityChanged += OnAimingIntensityChanged;
        }

        if (_turret != null)
        {
            _turret.OnStateChanged += OnTurretStateChanged;
            _turret.OnShot += OnTurretShot;
            _turret.OnAmmoChanged += OnAmmoChanged;

            _lastState = _turret.CurrentState;
            _lastAmmoCount = _turret.CurrentAmmo;
        }

        _targetGridIntensity = _gridMinIntensity;
        UpdateGridIntensity(0f);

        UpdateAmmoDisplay();

        AnimateStatus("SYSTEM ONLINE", new Color(0.2f, 0.9f, 0.8f, 0.9f));

        _animPlayer?.Play("Boot");
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    public void HideHUD()
    {
        _statusTween?.Kill();

        if (_reticle != null)
        {
            _reticle.OnAimingIntensityChanged -= OnAimingIntensityChanged;
            _reticle.Deinitialize();
        }

        if (_turret != null)
        {
            _turret.OnStateChanged -= OnTurretStateChanged;
            _turret.OnShot -= OnTurretShot;
            _turret.OnAmmoChanged -= OnAmmoChanged;
            _turret = null;
        }

        Visible = false;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    private void OnAimingIntensityChanged(float intensity)
    {
        _targetGridIntensity = Mathf.Lerp(_gridMinIntensity, _gridMaxIntensity, intensity);
    }

    private void OnTurretStateChanged(ShootingTurret.TurretState state)
    {
        if (state == _lastState) return;
        _lastState = state;

        switch (state)
        {
            case ShootingTurret.TurretState.Shooting:
                AnimateStatus("● FIRING", new Color(1f, 0.95f, 0.8f, 1f));
                FlashGrid(new Color(1f, 0.95f, 0.8f, 0.4f), 0.1f);
                break;

            case ShootingTurret.TurretState.Reloading:
                // Холодный цвет для охлаждения!
                AnimateStatus("❄ COOLING", new Color(0.5f, 0.8f, 1f, 0.9f));
                FlashGrid(new Color(0.4f, 0.7f, 1f, 0.3f), 0.15f);
                break;

            case ShootingTurret.TurretState.Broken:
                AnimateStatus("✕ CRITICAL", new Color(1f, 0.2f, 0.1f, 0.9f));
                FlashGrid(new Color(1f, 0.2f, 0.1f, 0.5f), 0.5f);
                SharedHUD.TriggerGlitch(0.8f, 0.3f);
                break;

            case ShootingTurret.TurretState.FiringCooldown:
                AnimateStatus("○ CYCLING", new Color(0.2f, 0.9f, 0.8f, 0.7f));
                break;

            case ShootingTurret.TurretState.Idle:
                if (_turret != null && _turret.CanShoot)
                    AnimateStatus("● READY", new Color(0.2f, 0.9f, 0.8f, 0.9f));
                else if (_turret != null && !_turret.HasAmmoInMag)
                    AnimateStatus("⚠ NO AMMO", new Color(1f, 0.3f, 0.2f, 0.9f));
                break;
        }
    }

    private void OnTurretShot()
    {
        RobotBus.Combat("SHOT_FIRED");
        FlashGrid(new Color(1f, 0.95f, 0.8f, 0.3f), 0.05f);
        // Глитч вызывается через TurretReticle -> SharedHUD
    }

    private void OnAmmoChanged()
    {
        if (_turret == null) return;
        _lastAmmoCount = _turret.CurrentAmmo;
        UpdateAmmoDisplay();

        if (!_turret.HasInfiniteAmmo)
        {
            float ratio = (float)_turret.CurrentAmmo / _turret.MagazineSize;
            if (ratio <= 0.1f && _turret.CurrentAmmo > 0)
                AnimateStatus("⚠ LOW AMMO", new Color(1f, 0.5f, 0.2f, 0.9f));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_turret == null) return;
        float dt = (float)delta;

        _currentGridIntensity = Mathf.Lerp(_currentGridIntensity, _targetGridIntensity, dt * _gridFadeSpeed);
        UpdateGridIntensity(_currentGridIntensity);

        UpdateDistanceDisplay();
    }

    private void UpdateDistanceDisplay()
    {
        if (_distanceLabel != null && _reticle != null)
        {
            float dist = _reticle.GetDisplayDistance();
            _distanceLabel.Text = dist > 2000 ? "----m" : $"{dist:0000}m";
        }
    }

    private void UpdateAmmoDisplay()
    {
        if (_ammoLabel == null || _turret == null) return;

        if (_turret.HasInfiniteAmmo)
        {
            _ammoLabel.Text = "∞";
            _ammoLabel.Modulate = new Color(0.2f, 0.9f, 0.8f);
        }
        else
        {
            _ammoLabel.Text = $"{_turret.CurrentAmmo:D2}/{_turret.MagazineSize:D2}";

            float ratio = (float)_turret.CurrentAmmo / _turret.MagazineSize;
            _ammoLabel.Modulate = ratio > 0.3f
                ? new Color(0.2f, 0.9f, 0.8f)
                : ratio > 0.1f
                    ? new Color(1f, 0.7f, 0.2f)
                    : new Color(1f, 0.3f, 0.2f);
        }

        if (_ammoBar != null && !_turret.HasInfiniteAmmo)
            _ammoBar.Value = (float)_turret.CurrentAmmo / _turret.MagazineSize * 100f;
    }

    private void AnimateStatus(string text, Color color)
    {
        if (_statusLabel == null) return;

        _statusTween?.Kill();
        _statusTween = CreateTween();
        _statusTween.SetParallel(true);

        _statusTween.TweenProperty(_statusLabel, "modulate", color, _statusFadeDuration);

        _targetStatusText = text;
        _displayedCharCount = 0;

        float typeDuration = text.Length / _statusTypeSpeed;

        _statusTween.TweenMethod(
            Callable.From<int>(UpdateStatusText),
            0, text.Length, typeDuration
        );
    }

    private void UpdateStatusText(int charCount)
    {
        _displayedCharCount = charCount;
        if (_statusLabel != null && _targetStatusText.Length > 0)
        {
            string displayed = _targetStatusText[..Mathf.Min(charCount, _targetStatusText.Length)];
            if (charCount < _targetStatusText.Length)
                displayed += (Time.GetTicksMsec() / 100 % 2 == 0) ? "_" : " ";
            _statusLabel.Text = displayed;
        }
    }

    private void UpdateGridIntensity(float intensity)
    {
        _gridMaterial?.SetShaderParameter("intensity", intensity);
    }

    private async void FlashGrid(Color flashColor, float duration)
    {
        if (_gridMaterial == null) return;

        Color originalLarge = (Color)_gridMaterial.GetShaderParameter("grid_color_large");
        Color originalSmall = (Color)_gridMaterial.GetShaderParameter("grid_color_small");

        _gridMaterial.SetShaderParameter("grid_color_large", flashColor);
        _gridMaterial.SetShaderParameter("grid_color_small", flashColor * 0.7f);

        await ToSignal(GetTree().CreateTimer(duration), SceneTreeTimer.SignalName.Timeout);

        if (_gridMaterial != null)
        {
            _gridMaterial.SetShaderParameter("grid_color_large", originalLarge);
            _gridMaterial.SetShaderParameter("grid_color_small", originalSmall);
        }
    }
}