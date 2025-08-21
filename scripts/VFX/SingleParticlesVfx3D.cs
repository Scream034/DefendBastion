using System;
using Godot;

namespace Game.VFX;

public partial class SingleParticlesVfx3D : BaseVfx3D
{
    public override event Action OnFinished;

    public override void Play()
    {
        if (Get(GpuParticles3D.PropertyName.Emitting).AsBool())
        {
            GD.PushWarning($"Already playing VFX: {Name}");
            return;
        }

        Connect(GpuParticles3D.SignalName.Finished, Callable.From(OnFinished), 4); // one-shot
        Set(GpuParticles3D.PropertyName.Emitting, true);
    }

    /// <summary>
    /// Остновливает VFX без вызова события OnFinished
    /// </summary>
    public override void Stop()
    {
        Set(GpuParticles3D.PropertyName.Emitting, false);
    }
}