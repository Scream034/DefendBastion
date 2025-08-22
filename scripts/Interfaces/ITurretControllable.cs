using Game.Turrets;
using Godot;

namespace Game.Interfaces;

public interface ITurretControllable
{
    void EnterTurret(BaseTurret turret);
    void ExitTurret(Vector3 exitPosition);
}
