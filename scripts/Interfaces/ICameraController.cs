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
    /// Возвращает узел Camera3D, связанный с этим контроллером.
    /// </summary>
    /// <returns>Активная камера.</returns>
    Camera3D GetCamera();

    /// <summary>
    /// Возвращает узел-владелец этого контроллера.
    /// </summary>
    IOwnerCameraController? GetCameraOwner();
}

#nullable disable