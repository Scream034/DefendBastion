using System;
using Godot;

namespace Game.VFX;

public partial class ShaderVFX3D : BaseVfx3D
{
    public override event Action OnFinished;

    [Export] private MeshInstance3D meshInstance;
    [Export] private float effectDuration = 0.5f;
    [Export] private bool autoPlay = false;
    [Export] private bool destroyOnFinish = true;

    private ShaderMaterial _shaderMaterial;
    private float _timePassed;
    private bool _isPlaying;

    public override void _Ready()
    {
        meshInstance ??= GetNode<MeshInstance3D>("MeshInstance3D");

        if (meshInstance?.Mesh?.GetSurfaceCount() > 0)
        {
            var material = meshInstance.Mesh.SurfaceGetMaterial(0);
            if (material is ShaderMaterial shaderMat)
            {
                _shaderMaterial = (ShaderMaterial)shaderMat.Duplicate();
                meshInstance.MaterialOverride = _shaderMaterial;
            }
            else
            {
                GD.PrintErr($"Material on {Name} is not a ShaderMaterial");
            }
        }

        if (autoPlay)
        {
            Play();
        }
    }

    public override void _Process(double delta)
    {
        if (!_isPlaying) return;

        _timePassed += (float)delta;
        float lifetime = 1.0f - (_timePassed / effectDuration);

        if (lifetime <= 0.0f)
        {
            FinishEffect();
            return;
        }

        _shaderMaterial?.SetShaderParameter("lifetime", lifetime);
    }

    public override void Play()
    {
        if (_isPlaying) return;

        _isPlaying = true;
        _timePassed = 0.0f;
        
        if (meshInstance != null)
        {
            meshInstance.Visible = true;
        }

        _shaderMaterial?.SetShaderParameter("lifetime", 1.0f);
    }

    public override void Stop()
    {
        if (!_isPlaying) return;

        _isPlaying = false;
        _timePassed = 0.0f;

        if (meshInstance != null)
        {
            meshInstance.Visible = false;
        }

        _shaderMaterial?.SetShaderParameter("lifetime", 0.0f);
    }

    private void FinishEffect()
    {
        _isPlaying = false;
        
        if (meshInstance != null)
        {
            meshInstance.Visible = false;
        }

        OnFinished?.Invoke();

        if (destroyOnFinish)
        {
            QueueFree();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>
    /// Устанавливает параметр шейдера
    /// </summary>
    public void SetShaderParameter(string paramName, Variant value)
    {
        _shaderMaterial?.SetShaderParameter(paramName, value);
    }

    /// <summary>
    /// Получает параметр шейдера
    /// </summary>
    public Variant GetShaderParameter(string paramName)
    {
        return _shaderMaterial?.GetShaderParameter(paramName) ?? new Variant();
    }
}