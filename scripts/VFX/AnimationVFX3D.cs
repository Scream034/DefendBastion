using System;
using Godot;

namespace Game.VFX;

[Obsolete("Доделать класс AnimaitonVFX3D на основе AnimaitonPlayer")]
public partial class AnimationVFX3D : BaseVfx3D
{
    public override event Action OnFinished;

    [Export] public AnimationPlayer AnimationPlayer { get; private set; }

    public override void Play()
    {
        AnimationPlayer.AnimationFinished += (_) => OnFinished?.Invoke();
        AnimationPlayer.Play("VFX");
    }

    /// <summary>
    /// Остновливает VFX без вызова события OnFinished
    /// </summary>
    public override void Stop()
    {
        AnimationPlayer.Stop();
        OnFinished?.Invoke();
    }
}