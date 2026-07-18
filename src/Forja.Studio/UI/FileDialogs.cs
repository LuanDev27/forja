using System.IO;
using Forja.Anvil.Scene;
using Forja.Core.Persistence;
using Forja.Studio.Editor;
using Godot;

namespace Forja.Studio.UI;

/// <summary>
/// Arquivo de cena (T040, RF-08): nova, abrir e salvar `.forja`, mais botões
/// de desfazer/refazer. Erros de carga/gravação aparecem num diálogo com
/// caminho e motivo (Artigo VII.3 — a mensagem já vem assim do
/// SceneSerializer). Trocar o documento só é permitido em Edit
/// (Loop.ReplaceDocument); os botões espelham essa guarda.
/// </summary>
public partial class FileDialogs : CanvasLayer
{
    private readonly EditorContext _ctx;
    private Button _new = null!;
    private Button _open = null!;
    private Button _save = null!;
    private Button _saveAs = null!;
    private Button _undo = null!;
    private Button _redo = null!;
    private Label _file = null!;
    private FileDialog _openDialog = null!;
    private FileDialog _saveDialog = null!;
    private AcceptDialog _error = null!;
    private ConfirmationDialog _confirmNew = null!;
    private string? _path;

    public FileDialogs(EditorContext ctx) => _ctx = ctx;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        margin.AddThemeConstantOverride("margin_top", 48);
        margin.AddThemeConstantOverride("margin_left", 8);
        AddChild(margin);

        var panel = new PanelContainer();
        margin.AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        _new = MakeButton(row, "Nova", () => _confirmNew.PopupCentered());
        _open = MakeButton(row, "Abrir…", () => _openDialog.PopupCentered());
        _save = MakeButton(row, "Salvar", SaveCurrent);
        _saveAs = MakeButton(row, "Salvar como…", () => _saveDialog.PopupCentered());
        _undo = MakeButton(row, "Desfazer", _ctx.Undo);
        _redo = MakeButton(row, "Refazer", _ctx.Redo);

        _file = new Label { Text = "(sem arquivo)" };
        row.AddChild(_file);

        _openDialog = MakeFileDialog("Abrir cena", FileDialog.FileModeEnum.OpenFile);
        _openDialog.FileSelected += LoadScene;

        _saveDialog = MakeFileDialog("Salvar cena", FileDialog.FileModeEnum.SaveFile);
        _saveDialog.FileSelected += SaveScene;

        _error = new AcceptDialog { Title = "Erro" };
        AddChild(_error);

        _confirmNew = new ConfirmationDialog
        {
            Title = "Nova cena",
            DialogText = "Descartar a cena atual e começar uma nova?",
        };
        _confirmNew.Confirmed += NewScene;
        AddChild(_confirmNew);
    }

    public override void _Process(double delta)
    {
        bool edit = _ctx.InEdit;
        _new.Disabled = !edit;
        _open.Disabled = !edit;
        _save.Disabled = !edit;
        _saveAs.Disabled = !edit;
        _undo.Disabled = !_ctx.Main.Loop.CanUndo;
        _redo.Disabled = !_ctx.Main.Loop.CanRedo;
        _file.Text = _path is null ? "(sem arquivo)" : Path.GetFileName(_path);
    }

    private void NewScene()
    {
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "Nova cena",
        };

        if (!_ctx.Main.Loop.ReplaceDocument(doc))
        {
            ShowError("Nova cena só é permitida no modo Edição.");
            return;
        }

        _path = null;
        _ctx.AfterDocumentReplaced();
    }

    private void LoadScene(string path)
    {
        var loaded = SceneSerializer.LoadFile(path);
        if (!loaded.Ok)
        {
            ShowError(loaded.Error!);
            return;
        }

        if (!_ctx.Main.Loop.ReplaceDocument(loaded.Value!))
        {
            ShowError("Carregar cena só é permitido no modo Edição.");
            return;
        }

        _path = path;
        _ctx.AfterDocumentReplaced();
    }

    private void SaveCurrent()
    {
        if (_path is null)
            _saveDialog.PopupCentered();
        else
            SaveScene(_path);
    }

    private void SaveScene(string path)
    {
        if (!path.EndsWith(".forja", System.StringComparison.OrdinalIgnoreCase))
            path += ".forja";

        var saved = SceneSerializer.SaveFile(_ctx.Main.Loop.Document, path);
        if (!saved.Ok)
        {
            ShowError(saved.Error!);
            return;
        }

        _path = path;
    }

    private void ShowError(string message)
    {
        _error.DialogText = message;
        _error.PopupCentered();
    }

    private Button MakeButton(Node parent, string text, System.Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += () => action();
        parent.AddChild(button);
        return button;
    }

    private FileDialog MakeFileDialog(string title, FileDialog.FileModeEnum mode)
    {
        var dialog = new FileDialog
        {
            Title = title,
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.forja ; Cena Forja" },
            Size = new Vector2I(700, 450),
        };
        AddChild(dialog);
        return dialog;
    }
}
