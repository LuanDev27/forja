using System;
using System.Collections.Generic;
using System.Linq;
using Forja.Anvil.Catalog;
using Godot;

namespace Forja.Studio.Editor;

/// <summary>
/// Painel de catálogo (T037, RF-02). Lista os tipos carregados de
/// catalog/devices/*.json em runtime (Artigo III.2 — nada hardcoded por tipo):
/// clicar num tipo arma a colocação; o clique no chão coloca (SelectionManager
/// mostra o ghost de preview). Shift mantém a colocação armada; Esc cancela.
/// </summary>
public partial class CatalogPanel : CanvasLayer
{
    private readonly EditorContext _ctx;
    private readonly List<string> _typeIds = new();
    private ItemList _list = null!;
    private Label _hint = null!;

    public CatalogPanel(EditorContext ctx) => _ctx = ctx;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        margin.AddThemeConstantOverride("margin_top", 88);
        margin.AddThemeConstantOverride("margin_left", 8);
        AddChild(margin);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) };
        margin.AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);
        box.AddChild(new Label { Text = "Catálogo" });

        _list = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 330),
            SelectMode = ItemList.SelectModeEnum.Single,
        };
        box.AddChild(_list);

        _hint = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(234, 0),
        };
        box.AddChild(_hint);

        foreach (var type in _ctx.Main.Catalog.All
            .OrderBy(t => t.Category)
            .ThenBy(t => t.DisplayName, StringComparer.Ordinal))
        {
            _typeIds.Add(type.TypeId);
            _list.AddItem($"{CategoryText(type.Category)} · {type.DisplayName}");
        }

        _list.ItemSelected += index => _ctx.ArmedTypeId = _typeIds[(int)index];
    }

    public override void _Process(double delta)
    {
        Visible = _ctx.InEdit;

        if (_ctx.ArmedTypeId is { } armed && _ctx.Main.Catalog.TryGet(armed, out var type))
        {
            _hint.Text = $"Colocando: {type.DisplayName} — clique no chão. " +
                         "Shift mantém; Esc cancela.";
        }
        else
        {
            if (_list.IsAnythingSelected())
                _list.DeselectAll();
            _hint.Text = "Clique num tipo e depois no chão para colocar.";
        }
    }

    private static string CategoryText(DeviceCategory category) => category switch
    {
        DeviceCategory.Passive => "Estrutura",
        DeviceCategory.Transport => "Transporte",
        DeviceCategory.Sensor => "Sensor",
        DeviceCategory.Actuator => "Atuador",
        DeviceCategory.SourceSink => "Origem/Fim",
        DeviceCategory.Part => "Peça",
        DeviceCategory.Hmi => "HMI",
        _ => category.ToString(),
    };
}
