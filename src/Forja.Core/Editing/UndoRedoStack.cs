using Forja.Anvil.Scene;
using Forja.Core.Persistence;

namespace Forja.Core.Editing;

/// <summary>
/// Histórico de edição por memento (T036): guarda snapshots do
/// <see cref="SceneDocument"/>, não inversos por comando — assim o undo
/// restaura o estado EXATO, sem risco de inverso mal implementado. Clona via
/// round-trip de serialização (também normaliza a ordem canônica). Mantém até
/// <see cref="MaxUndoLevels"/> níveis de desfazer; o mais antigo cai fora.
/// </summary>
public sealed class UndoRedoStack
{
    public const int MaxUndoLevels = 100;

    private readonly List<SceneDocument> _states = new();
    private int _index;

    public UndoRedoStack(SceneDocument initial)
    {
        _states.Add(Clone(initial));
        _index = 0;
    }

    public SceneDocument Current => _states[_index];

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _states.Count - 1;

    /// <summary>Quantos undos estão disponíveis agora.</summary>
    public int UndoDepth => _index;

    /// <summary>Aplica o comando, empilha o novo estado e descarta o "redo".</summary>
    public SceneDocument Execute(IEditorCommand command)
    {
        var next = Clone(command.Apply(Current));

        // Um ramo novo invalida o caminho de redo à frente.
        if (_index < _states.Count - 1)
            _states.RemoveRange(_index + 1, _states.Count - _index - 1);

        _states.Add(next);
        _index++;

        // Teto de níveis: mantém no máximo MaxUndoLevels + 1 snapshots.
        if (_index > MaxUndoLevels)
        {
            _states.RemoveAt(0);
            _index--;
        }

        return Current;
    }

    public SceneDocument Undo()
    {
        if (CanUndo)
            _index--;
        return Current;
    }

    public SceneDocument Redo()
    {
        if (CanRedo)
            _index++;
        return Current;
    }

    /// <summary>Clone profundo por serialização — a mesma normalização do save,
    /// então dois estados "iguais" têm exatamente o mesmo JSON canônico.</summary>
    private static SceneDocument Clone(SceneDocument doc) =>
        SceneSerializer.Load(SceneSerializer.Save(doc), "<undo>").Require();
}
