using System.Linq;
using System.Text;
using System.Text.Json;
using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Core.Editing;
using Godot;

namespace Forja.Studio.Editor;

/// <summary>
/// Painel de propriedades (T039, RF-02). Gerado 100% de
/// <see cref="DeviceTypeDef.ParamDefs"/> — sem UI hardcoded por tipo (Artigo
/// III.2). Também expõe o endereço de cada porta (ReassignAddressCommand,
/// RF-05 — pendência da US2): a área vem da direção da porta (In→DiscreteInput,
/// Out→Coil, v1 digital-only), o usuário digita só o offset.
/// Toda edição vira comando via EditorContext; conflitos de endereço são
/// apontados pelo validador ao tentar Rodar (Artigo VI.3).
/// </summary>
public partial class ParamsPanel : CanvasLayer
{
    private readonly EditorContext _ctx;
    private VBoxContainer _content = null!;
    private string _signature = "\0";

    public ParamsPanel(EditorContext ctx) => _ctx = ctx;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        margin.AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);
        box.AddChild(new Label { Text = "Propriedades" });

        _content = new VBoxContainer();
        box.AddChild(_content);
    }

    public override void _Process(double delta)
    {
        Visible = _ctx.InEdit;
        if (!Visible)
            return;

        string signature = BuildSignature();
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren())
            child.Free();

        if (ActiveDevice() is not { } device
            || !_ctx.Main.Catalog.TryGet(device.TypeId, out var type))
        {
            _content.AddChild(new Label { Text = "Nenhum dispositivo selecionado." });
            return;
        }

        _content.AddChild(new Label { Text = $"{type.DisplayName} #{device.Id}" });
        _content.AddChild(new Label
        {
            Text = $"Pos ({device.Transform.Pos.X:0.0#}, {device.Transform.Pos.Y:0.0#}, " +
                   $"{device.Transform.Pos.Z:0.0#}) · Rot {device.Transform.RotY:0}°",
        });

        if (type.ParamDefs.Count > 0)
        {
            _content.AddChild(new Label { Text = "Parâmetros" });
            var grid = new GridContainer { Columns = 2 };
            _content.AddChild(grid);
            foreach (var def in type.ParamDefs)
            {
                grid.AddChild(new Label { Text = def.Name });
                grid.AddChild(BuildParamControl(device, def));
            }
        }

        if (type.Ports.Count > 0)
        {
            _content.AddChild(new Label { Text = "Endereços de I/O" });
            var grid = new GridContainer { Columns = 3 };
            _content.AddChild(grid);
            foreach (var port in type.Ports)
                AddPortRow(grid, device, port);
        }
    }

    private Control BuildParamControl(DeviceInstance device, ParamDef def)
    {
        uint id = device.Id;
        JsonElement? current = device.Params.TryGetValue(def.Name, out var el)
            ? el
            : def.Default;

        switch (def.Type)
        {
            case "bool":
            {
                var check = new CheckBox
                {
                    ButtonPressed = current is { ValueKind: JsonValueKind.True },
                };
                check.Toggled += value => Commit(id, def.Name, value);
                return check;
            }
            case "int" or "float":
            {
                var spin = new SpinBox
                {
                    MinValue = def.Min ?? -1e9,
                    MaxValue = def.Max ?? 1e9,
                    Step = def.Type == "int" ? 1 : 0.01,
                    CustomMinimumSize = new Vector2(110, 0),
                };
                if (current is { ValueKind: JsonValueKind.Number } number)
                    spin.Value = number.GetDouble();
                spin.ValueChanged += value =>
                {
                    if (def.Type == "int")
                        Commit(id, def.Name, (int)value);
                    else
                        Commit(id, def.Name, (float)value);
                };
                return spin;
            }
            case "enum":
            {
                var option = new OptionButton();
                string? selected = current is { ValueKind: JsonValueKind.String } s
                    ? s.GetString()
                    : null;
                foreach (string value in def.Values ?? Enumerable.Empty<string>())
                {
                    option.AddItem(value);
                    if (value == selected)
                        option.Selected = option.ItemCount - 1;
                }
                option.ItemSelected += index => Commit(id, def.Name, option.GetItemText((int)index));
                return option;
            }
            default: // "string"
            {
                var edit = new LineEdit
                {
                    Text = current is { ValueKind: JsonValueKind.String } str
                        ? str.GetString()
                        : "",
                    CustomMinimumSize = new Vector2(110, 0),
                };
                edit.TextSubmitted += text => Commit(id, def.Name, text);
                return edit;
            }
        }
    }

    private void AddPortRow(GridContainer grid, DeviceInstance device, PortDef port)
    {
        uint id = device.Id;
        var tag = _ctx.Main.Loop.Document.IoMap
            .FirstOrDefault(t => t.DeviceId == id && t.PortName == port.PortName);
        var area = port.Direction == IoDirection.In ? IoArea.DiscreteInput : IoArea.Coil;

        grid.AddChild(new Label
        {
            Text = $"{port.PortName} ({(port.Direction == IoDirection.In ? "entrada" : "saída")})",
        });

        var offset = new LineEdit
        {
            Text = tag?.Address.Offset.ToString() ?? "",
            PlaceholderText = "n/d",
            CustomMinimumSize = new Vector2(52, 0),
        };
        // Sem preempção de assinatura aqui: o rebuild atualiza o rótulo IEC.
        offset.TextSubmitted += text =>
        {
            if (ushort.TryParse(text, out ushort value))
                _ctx.Execute(new ReassignAddressCommand(id, port.PortName, new IoAddress(area, value)));
        };
        grid.AddChild(offset);

        grid.AddChild(new Label { Text = tag?.Address.ToDisplay() ?? "—" });
    }

    /// <summary>Comita e adianta a assinatura: o valor no controle já é o novo,
    /// então pular o rebuild preserva o foco de quem está digitando.</summary>
    private void Commit<T>(uint id, string name, T value)
    {
        if (_ctx.Execute(new EditParamCommand(id, name, JsonSerializer.SerializeToElement(value))))
            _signature = BuildSignature();
    }

    private DeviceInstance? ActiveDevice()
    {
        if (_ctx.ActiveId is not { } id)
            return null;
        return _ctx.Main.Loop.Document.Devices.FirstOrDefault(d => d.Id == id);
    }

    private string BuildSignature()
    {
        if (ActiveDevice() is not { } device)
            return "";

        var sb = new StringBuilder();
        sb.Append(device.Id).Append('|').Append(device.TypeId).Append('|')
          .Append(device.Transform.Pos.X).Append(',')
          .Append(device.Transform.Pos.Y).Append(',')
          .Append(device.Transform.Pos.Z).Append(',')
          .Append(device.Transform.RotY);
        foreach (var (name, value) in device.Params.OrderBy(p => p.Key, System.StringComparer.Ordinal))
            sb.Append('|').Append(name).Append('=').Append(value.GetRawText());
        foreach (var tag in _ctx.Main.Loop.Document.IoMap)
        {
            if (tag.DeviceId == device.Id)
                sb.Append('|').Append(tag.PortName).Append('@').Append(tag.Address.ToIec());
        }
        return sb.ToString();
    }
}
