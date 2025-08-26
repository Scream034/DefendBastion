namespace Game.Entity.AI;

/// <summary>
/// Интерфейс для конкретного действия атаки (выстрел, удар и т.д.).
/// Не содержит логики движения или перезарядки.
/// </summary>
public interface IAttackAction
{
    void Execute(AIEntity attacker, LivingEntity target);
}