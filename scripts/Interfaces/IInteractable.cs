namespace Game.Interfaces;

/// <summary>
/// Интерфейс для объектов, с которыми игрок может взаимодействовать.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Выполняет основное действие взаимодействия.
    /// </summary>
    /// <param name="character">Персонаж, инициировавший взаимодействие.</param>
    void Interact(Player.Player character);

    /// <summary>
    /// Возвращает текст-подсказку для взаимодействия.
    /// </summary>
    /// <returns>Строка с подсказкой (например, "Нажмите Е, чтобы использовать турель").</returns>
    string GetInteractionText();
}