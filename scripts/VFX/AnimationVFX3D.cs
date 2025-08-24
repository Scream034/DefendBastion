using System;
using Godot;

namespace Game.VFX;

public partial class AnimationVFX3D : BaseVfx3D
{
    public override event Action OnFinished;

    public override void Play()
    {
        
    }

    /// <summary>
    /// Остновливает VFX без вызова события OnFinished
    /// </summary>
    public override void Stop()
    {
    }
}