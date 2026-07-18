using System.Collections.Generic;
using System.Linq;
using Forja.Anvil.Contracts;
using Forja.Core.Editing;
using Forja.Studio.Rendering;

namespace Forja.Studio.Editor;

/// <summary>
/// Estado compartilhado do editor (US3): seleção, tipo armado para colocação e
/// o funil único de mutação — toda alteração de cena passa por
/// <see cref="Execute"/> (IEditorCommand → SimulationLoop, Artigo II.2); a UI
/// nunca toca o Document direto.
/// </summary>
public sealed class EditorContext
{
    public EditorContext(Main main, SceneView view)
    {
        Main = main;
        View = view;
    }

    public Main Main { get; }

    public SceneView View { get; }

    /// <summary>Ids selecionados. O último clicado é o <see cref="ActiveId"/>.</summary>
    public HashSet<uint> Selection { get; } = new();

    /// <summary>Alvo dos gizmos e do painel de parâmetros.</summary>
    public uint? ActiveId { get; private set; }

    /// <summary>Tipo do catálogo aguardando clique de colocação (null = nenhum).</summary>
    public string? ArmedTypeId { get; set; }

    public bool InEdit => Main.HasLoop && Main.Loop.Mode == SimMode.Edit;

    public void Select(uint id, bool additive)
    {
        if (!additive)
        {
            Selection.Clear();
            Selection.Add(id);
            ActiveId = id;
        }
        else if (!Selection.Add(id))
        {
            Selection.Remove(id);
            if (ActiveId == id)
                ActiveId = Selection.Count > 0 ? Selection.First() : null;
        }
        else
        {
            ActiveId = id;
        }

        View.SetSelected(Selection);
    }

    public void SelectExactly(IEnumerable<uint> ids)
    {
        Selection.Clear();
        uint? last = null;
        foreach (uint id in ids)
        {
            Selection.Add(id);
            last = id;
        }

        ActiveId = last;
        View.SetSelected(Selection);
    }

    public void ClearSelection() => SelectExactly(Enumerable.Empty<uint>());

    public bool Execute(IEditorCommand command)
    {
        if (!Main.Loop.ExecuteEdit(command))
            return false;
        AfterDocumentChange();
        return true;
    }

    public void Undo()
    {
        if (Main.Loop.Undo())
            AfterDocumentChange();
    }

    public void Redo()
    {
        if (Main.Loop.Redo())
            AfterDocumentChange();
    }

    /// <summary>Cena inteira trocada (carregar/nova) — descarta seleção e
    /// colocação armada e redesenha.</summary>
    public void AfterDocumentReplaced()
    {
        ArmedTypeId = null;
        Selection.Clear();
        ActiveId = null;
        View.SetSelected(Selection);
        View.Rebuild();
    }

    /// <summary>Depois de qualquer comando/undo/redo: poda ids que sumiram do
    /// documento e reconstrói o visual.</summary>
    private void AfterDocumentChange()
    {
        var doc = Main.Loop.Document;
        Selection.RemoveWhere(id => doc.Devices.All(d => d.Id != id));
        if (ActiveId is { } active && !Selection.Contains(active))
            ActiveId = Selection.Count > 0 ? Selection.First() : null;

        View.SetSelected(Selection);
        View.Rebuild();
    }
}
