using Forja.Anvil.Scene;

namespace Forja.Core.Editing;

/// <summary>
/// Comando de edição de cena (T036, research R7). Transforma PURO do
/// <see cref="SceneDocument"/>: recebe o documento atual e devolve o próximo,
/// sem efeitos colaterais nem dependência de engine (por isso vive na camada
/// de núcleo, testável sem Godot). O undo/redo é responsabilidade da
/// <see cref="UndoRedoStack"/> (memento) — o comando só sabe ir adiante.
/// Editar cena só é permitido em modo Edit; a guarda fica no SimulationLoop.
/// </summary>
public interface IEditorCommand
{
    /// <summary>Rótulo curto para o histórico da UI.</summary>
    string Label { get; }

    /// <summary>Aplica a transformação e devolve o novo documento.</summary>
    SceneDocument Apply(SceneDocument doc);
}
