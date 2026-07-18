using System.Collections.Generic;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Io;
using Godot;

namespace Forja.Studio.UI;

/// <summary>
/// Tabela de I/O ao vivo (T031, RF-05). Lê <see cref="IoTable.BuildView"/> a
/// cada frame (Artigo II.2 — só leitura) e mostra cada ponto em notação dupla
/// (%IX0.0 (DI 0)), direção e valor atual. Cada linha permite FORÇAR o bit —
/// ciclo —/1/0 via <see cref="ForceIoCommand"/> — com indicação visual do
/// override (fundo âmbar). Em Edit não há tabela: a IoTable só existe em Run.
/// </summary>
public partial class IoTablePanel : CanvasLayer
{
    private static readonly Color ForcedColor = new(0.90f, 0.60f, 0.15f);
    private static readonly Color OnColor = new(0.35f, 0.85f, 0.45f);
    private static readonly Color OffColor = new(0.45f, 0.46f, 0.50f);

    private readonly Main _main;
    private readonly Dictionary<IoAddress, Row> _rows = new();

    private VBoxContainer _list = null!;
    private Label _empty = null!;
    private string _signature = "";

    public IoTablePanel(Main main) => _main = main;

    public override void _Ready()
    {
        // Margem tela-cheia com Ignore (ver FileDialogs): âncora de canto pura
        // posicionava o painel fora da borda direita da janela.
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_top", 48);
        margin.AddThemeConstantOverride("margin_right", 8);
        AddChild(margin);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(320, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        margin.AddChild(panel);

        var outer = new VBoxContainer();
        panel.AddChild(outer);

        outer.AddChild(new Label { Text = "Tabela de I/O" });

        _empty = new Label { Text = "Entre em Rodar para mapear a I/O." };
        outer.AddChild(_empty);

        _list = new VBoxContainer();
        outer.AddChild(_list);
    }

    public override void _Process(double delta)
    {
        var io = _main.Loop.Io;
        if (io is null)
        {
            if (_rows.Count > 0)
                ClearRows();
            _empty.Visible = true;
            return;
        }

        var view = io.BuildView();
        _empty.Visible = view.Count == 0;

        string signature = BuildSignature(view);
        if (signature != _signature)
        {
            RebuildRows(view);
            _signature = signature;
        }

        foreach (var point in view)
        {
            if (_rows.TryGetValue(point.Address, out var row))
                row.Update(point);
        }
    }

    private void RebuildRows(IReadOnlyList<IoPointView> view)
    {
        ClearRows();
        foreach (var point in view)
        {
            var row = new Row(point.Address, OnForce);
            _rows[point.Address] = row;
            _list.AddChild(row.Node);
        }
    }

    private void ClearRows()
    {
        foreach (var child in _list.GetChildren())
            child.Free();
        _rows.Clear();
        _signature = "";
    }

    /// <summary>Ciclo de força pedido pela linha: null→true→false→null.</summary>
    private void OnForce(IoAddress address, bool? value) =>
        _main.Loop.Enqueue(new ForceIoCommand(address, value));

    private static string BuildSignature(IReadOnlyList<IoPointView> view)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in view)
            sb.Append(p.Address.Area).Append(p.Address.Offset).Append('|');
        return sb.ToString();
    }

    /// <summary>Uma linha da tabela: endereço · direção · valor · botão forçar.</summary>
    private sealed class Row
    {
        private readonly IoAddress _address;
        private readonly System.Action<IoAddress, bool?> _onForce;
        private readonly Label _value;
        private readonly Button _force;
        private bool? _forcedTarget;

        public HBoxContainer Node { get; }

        public Row(IoAddress address, System.Action<IoAddress, bool?> onForce)
        {
            _address = address;
            _onForce = onForce;

            Node = new HBoxContainer();
            Node.AddThemeConstantOverride("separation", 8);

            Node.AddChild(new Label
            {
                Text = address.ToDisplay(),
                CustomMinimumSize = new Vector2(150, 0),
            });

            _value = new Label { CustomMinimumSize = new Vector2(70, 0) };
            Node.AddChild(_value);

            _force = new Button { CustomMinimumSize = new Vector2(60, 0) };
            _force.Pressed += Cycle;
            Node.AddChild(_force);
        }

        public void Update(IoPointView point)
        {
            _value.Text = point.Value ? "● 1" : "○ 0";
            _value.AddThemeColorOverride("font_color", point.Value ? OnColor : OffColor);

            // O core confirma o estado forçado — a UI apenas reflete.
            if (!point.Forced)
                _forcedTarget = null;

            _force.Text = _forcedTarget switch
            {
                true => "Forçar 1",
                false => "Forçar 0",
                _ => "Livre",
            };
            SetForcedTint(point.Forced);
        }

        private void Cycle()
        {
            _forcedTarget = _forcedTarget switch
            {
                null => true,
                true => false,
                false => null,
            };
            _onForce(_address, _forcedTarget);
        }

        private void SetForcedTint(bool forced)
        {
            var style = new StyleBoxFlat
            {
                BgColor = forced ? ForcedColor : new Color(0.25f, 0.25f, 0.28f),
            };
            _force.AddThemeStyleboxOverride("normal", style);
        }
    }
}
