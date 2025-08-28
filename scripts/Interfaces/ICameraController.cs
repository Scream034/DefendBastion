#nullable enable

using Godot;

namespace Game.Interfaces;

/// <summary>
/// Определяет контракт для любого объекта, который может управлять основной камерой игрока
/// и обрабатывать ввод от мыши.
/// </summary>
public interface ICameraController
{
    /// <summary>
    /// Вызывается, когда этот контроллер становится активным.
    /// </summary>
    void Activate();

    /// <summary>
    /// Вызывается, когда этот контроллер перестает быть активным.
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Обрабатывает движение мыши.
    /// </summary>
    /// <param name="mouseDelta">Вектор относительного смещения мыши.</param>
    void HandleMouseInput(Vector2 mouseDelta);

    /// <summary>
    /// Обрабатывает движение мыши с поддержкой плавного возврата тряски.
    /// </summary>
    /// <param name="mouseDelta">Вектор относительного смещения мыши.</param>
    /// <param name="delta">Время с последнего кадра для плавных анимаций.</param>
    void HandleMouseInput(Vector2 mouseDelta, float delta);

    /// <summary>
    /// Возвращает узел Camera3D, связанный с этим контроллером.
    /// </summary>
    /// <returns>Активная камера.</returns>
    Camera3D GetCamera();

    /// <summary>
    /// Возвращает узел-владелец этого контроллера.
    /// </summary>
    IOwnerCameraController? GetCameraOwner();

    /// <summary>
    /// Применяет эффект тряски к камере.
    /// </summary>
    /// <param name="duration">Продолжительность тряски.</param>
    /// <param name="strength">Интенсивность тряски.</param>
    void ApplyShake(float duration, float strength);
}

#nullable disable