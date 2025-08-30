using Game.Turrets;
using Godot;

namespace Game.Interfaces;

public interface ITurretControllable
{
    void EnterTurret(ControllableTurret turret);
    void ExitTurret(Vector3 exitPosition);
}