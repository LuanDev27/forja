using System;
using System.Text.Json;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Editing;
using Forja.Studio.Editor;
using Godot;

namespace Forja.Studio.UI;

/// <summary>
/// Painel de conexão com o PLC (T051, RF-06). Edita a ConnectionConfig da
/// cena via <see cref="SetConnectionCommand"/> (com undo, só em Edit) e
/// mostra o estado do driver a qualquer momento:
/// Desconectado / Aguardando master / Conectado / Erro.
/// </summary>
public partial class ConnectionPanel : CanvasLayer
{
    private static readonly string[] DriverKeys =
    {
        ConnectionConfig.NullDriverKey,
        ConnectionConfig.ModbusTcpServerKey,
        ConnectionConfig.ModbusTcpClientKey,
    };

    private readonly EditorContext _ctx;

    private OptionButton _driver = null!;
    private LineEdit _bind = null!;
    private LineEdit _host = null!;
    private SpinBox _port = null!;
    private SpinBox _unit = null!;
    private SpinBox _timeout = null!;
    private SpinBox _inputBase = null!;
    private Label _bindLabel = null!;
    private Label _hostLabel = null!;
    private Label _inputBaseLabel = null!;
    private Label _status = null!;
    private string _signature = "\0";
    private bool _populating;

    public ConnectionPanel(EditorContext ctx) => _ctx = ctx;

    public override void _Ready()
    {
        // Margem tela-cheia com Ignore (ver FileDialogs): âncora de canto pura
        // posicionava o painel abaixo da borda inferior da janela.
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(280, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
        };
        margin.AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);
        box.AddChild(new Label { Text = "Conexão PLC" });

        var grid = new GridContainer { Columns = 2 };
        box.AddChild(grid);

        grid.AddChild(new Label { Text = "Driver" });
        _driver = new OptionButton();
        _driver.AddItem("Nenhum (Tabela de I/O)");
        _driver.AddItem("Servidor Modbus TCP");
        _driver.AddItem("Cliente Modbus TCP");
        _driver.ItemSelected += _ => Commit();
        grid.AddChild(_driver);

        _bindLabel = AddLabel(grid, "Escutar em");
        _bind = new LineEdit { CustomMinimumSize = new Vector2(130, 0) };
        _bind.TextSubmitted += _ => Commit();
        grid.AddChild(_bind);

        _hostLabel = AddLabel(grid, "IP do PLC");
        _host = new LineEdit { CustomMinimumSize = new Vector2(130, 0) };
        _host.TextSubmitted += _ => Commit();
        grid.AddChild(_host);

        grid.AddChild(new Label { Text = "Porta" });
        _port = MakeSpin(grid, 1, 65535, 1);

        grid.AddChild(new Label { Text = "Unit id" });
        _unit = MakeSpin(grid, 1, 247, 1);

        grid.AddChild(new Label { Text = "Timeout (ms)" });
        _timeout = MakeSpin(grid, 100, 60000, 100);

        _inputBaseLabel = AddLabel(grid, "Coil base (entradas)");
        _inputBase = MakeSpin(grid, 0, 65535, 1);

        _status = new Label();
        box.AddChild(_status);
    }

    public override void _Process(double delta)
    {
        var loop = _ctx.Main.Loop;
        bool edit = _ctx.InEdit;

        string signature = JsonSerializer.Serialize(loop.Document.Connection);
        if (signature != _signature)
        {
            _signature = signature;
            Populate(loop.Document.Connection);
        }

        string key = DriverKeys[Math.Clamp(_driver.Selected, 0, DriverKeys.Length - 1)];
        bool isServer = key == ConnectionConfig.ModbusTcpServerKey;
        bool isClient = key == ConnectionConfig.ModbusTcpClientKey;
        bool isNull = key == ConnectionConfig.NullDriverKey;

        _driver.Disabled = !edit;
        _bind.Editable = edit;
        _host.Editable = edit;
        _port.Editable = edit && !isNull;
        _unit.Editable = edit && !isNull;
        _timeout.Editable = edit && !isNull;
        _inputBase.Editable = edit && isClient;

        _bindLabel.Visible = isServer;
        _bind.Visible = isServer;
        _hostLabel.Visible = isClient;
        _host.Visible = isClient;
        _inputBaseLabel.Visible = isClient;
        _inputBase.Visible = isClient;

        (string text, Color color) = loop.DriverState switch
        {
            DriverState.Stopped => ("Desconectado", new Color(0.6f, 0.6f, 0.6f)),
            DriverState.Starting => ("Aguardando master…", new Color(0.85f, 0.75f, 0.35f)),
            DriverState.Ready => ("Conectado", new Color(0.45f, 0.75f, 0.45f)),
            DriverState.Faulted => ("Erro", new Color(0.85f, 0.4f, 0.35f)),
            _ => (loop.DriverState.ToString(), new Color(0.6f, 0.6f, 0.6f)),
        };
        _status.Text = $"Estado: {text}";
        _status.AddThemeColorOverride("font_color", color);
    }

    /// <summary>Reflete a ConnectionConfig da cena nos controles (carga,
    /// undo, cena nova) sem disparar commits de volta.</summary>
    private void Populate(ConnectionConfig config)
    {
        _populating = true;
        _driver.Selected = Math.Max(0, Array.IndexOf(DriverKeys, config.Driver));
        _bind.Text = config.BindAddress;
        _host.Text = config.Host;
        _port.Value = config.Port;
        _unit.Value = config.UnitId;
        _timeout.Value = config.TimeoutMs;
        _inputBase.Value = config.InputBaseOffset;
        _populating = false;
    }

    private void Commit()
    {
        if (_populating || !_ctx.InEdit)
            return;

        var config = new ConnectionConfig
        {
            Driver = DriverKeys[Math.Clamp(_driver.Selected, 0, DriverKeys.Length - 1)],
            BindAddress = _bind.Text,
            Host = _host.Text,
            Port = (ushort)_port.Value,
            UnitId = (byte)_unit.Value,
            TimeoutMs = (int)_timeout.Value,
            InputBaseOffset = (ushort)_inputBase.Value,
        };

        if (_ctx.Main.Loop.ExecuteEdit(new SetConnectionCommand(config)))
            _signature = JsonSerializer.Serialize(_ctx.Main.Loop.Document.Connection);
    }

    private static Label AddLabel(GridContainer grid, string text)
    {
        var label = new Label { Text = text };
        grid.AddChild(label);
        return label;
    }

    private SpinBox MakeSpin(GridContainer grid, double min, double max, double step)
    {
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step };
        spin.ValueChanged += _ => Commit();
        grid.AddChild(spin);
        return spin;
    }
}
