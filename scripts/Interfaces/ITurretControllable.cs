using Game.Turrets;

namespace Game.Interfaces;

public interface ITurretControllable
{
    void EnterTurret(BaseTurret turret);
    void ExitTurret();
}
