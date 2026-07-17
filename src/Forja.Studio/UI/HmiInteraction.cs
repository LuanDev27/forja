using System.Collections.Generic;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Godot;

namespace Forja.Studio.UI;

/// <summary>
/// Painel de HMI (T033, RF-03). Para cada dispositivo de HMI com porta de
/// entrada na cena cria um controle clicável que ENFILEIRA
/// <see cref="HmiCommand"/> (Artigo II.2 — a UI nunca mexe no estado direto):
/// botão de comando é momentâneo (pressiona/solta); chave seletora alterna e
/// mantém. As luzes indicadoras (saída) acendem na cena 3D, não aqui.
/// </summary>
public partial class HmiInteraction : CanvasLayer
{
    private readonly Main _main;
    private VBoxContainer _list = null!;
    private Label _empty = null!;
    private string _signature = "";

    public HmiInteraction(Main main) => _main = main;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(240, 0) };
        margin.AddChild(panel);

        var outer = new VBoxContainer();
        panel.AddChild(outer);
        outer.AddChild(new Label { Text = "HMI" });

        _empty = new Label { Text = "Nenhum controle de HMI na cena." };
        outer.AddChild(_empty);

        _list = new VBoxContainer();
        outer.AddChild(_list);
    }

    public override void _Process(double delta)
    {
        string signature = BuildSignature();
        if (signature != _signature)
        {
            Rebuild();
            _signature = signature;
        }
    }

    private void Rebuild()
    {
        foreach (var child in _list.GetChildren())
            child.Free();

        int controls = 0;
        foreach (var device in _main.Loop.Document.Devices)
        {
            if (!_main.Catalog.TryGet(device.TypeId, out var type) || type.Category != DeviceCategory.Hmi)
                continue;

            foreach (var port in type.Ports)
            {
                if (port.Direction != IoDirection.In)
                    continue; // luzes (Out) acendem na cena, não têm controle
                _list.AddChild(BuildControl(device, type, port.PortName));
                controls++;
            }
        }

        _empty.Visible = controls == 0;
    }

    private Control BuildControl(DeviceInstance device, DeviceTypeDef type, string port)
    {
        uint id = device.Id;
        string label = $"{type.DisplayName} #{id}";

        // "pressed" = botão momentâneo; demais entradas de HMI = chave que mantém.
        if (port == "pressed")
        {
            var button = new Button { Text = label, ToggleMode = false };
            button.ButtonDown += () => _main.Loop.Enqueue(new HmiCommand(id, port, true));
            button.ButtonUp += () => _main.Loop.Enqueue(new HmiCommand(id, port, false));
            return button;
        }

        var toggle = new CheckButton { Text = label };
        toggle.Toggled += pressed => _main.Loop.Enqueue(new HmiCommand(id, port, pressed));
        return toggle;
    }

    private string BuildSignature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var device in _main.Loop.Document.Devices)
        {
            if (_main.Catalog.TryGet(device.TypeId, out var type) && type.Category == DeviceCategory.Hmi)
                sb.Append(device.Id).Append(':').Append(device.TypeId).Append('|');
        }
        return sb.ToString();
    }
}
