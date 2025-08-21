using System;
using Godot;

namespace Game.VFX;

public abstract partial class BaseVfx3D : Node3D
{
    public abstract event Action OnFinished;
    public abstract void Play();
    public abstract void Stop();
}