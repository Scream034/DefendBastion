using Game.Projectiles;
using Godot;

namespace Game.Interfaces
{
    public interface IShooter
    {
        bool Shoot();
        Node3D GetShootInitiator();
    }
}
