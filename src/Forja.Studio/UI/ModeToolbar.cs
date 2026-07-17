using Forja.Anvil.Contracts;
using Godot;

namespace Forja.Studio.UI;

/// <summary>
/// Toolbar de modos (T024, RF-01). UI fina (Artigo II.2): só ENFILEIRA
/// comandos — o loop decide no próximo tick — e LÊ modo/tick/driver
/// para exibir. Nenhum estado de simulação vive aqui.
/// </summary>
public partial class ModeToolbar : CanvasLayer
{
    private readonly Main _main;

    private Button _edit = null!;
    private Button _run = null!;
    private Button _pause = null!;
    private Button _step = null!;
    private Label _status = null!;

    public ModeToolbar(Main main) => _main = main;

    public override void _Ready()
    {
        var panel = new PanelContainer { Name = "Panel" };
        panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        _edit = MakeButton(row, "Editar", SimMode.Edit);
        _run = MakeButton(row, "Rodar", SimMode.Run);
        _pause = MakeButton(row, "Pausar", SimMode.Pause);
        _step = MakeButton(row, "Passo", SimMode.Step);

        _status = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.AddChild(_status);
    }

    public override void _Process(double delta)
    {
        var loop = _main.Loop;

        // Espelha as guardas da máquina de modos (data-model §8).
        _edit.Disabled = loop.Mode == SimMode.Edit;
        _run.Disabled = loop.Mode is not (SimMode.Edit or SimMode.Pause);
        _pause.Disabled = loop.Mode != SimMode.Run;
        _step.Disabled = loop.Mode != SimMode.Pause;

        _status.Text = $"Modo: {ModeText(loop.Mode)} · Tick: {loop.TickNumber} · " +
                       $"Driver: {DriverText(loop.DriverState)}";
    }

    private Button MakeButton(Node parent, string text, SimMode target)
    {
        var button = new Button { Text = text };
        button.Pressed += () => _main.Loop.Enqueue(new SetModeCommand(target));
        parent.AddChild(button);
        return button;
    }

    private static string ModeText(SimMode mode) => mode switch
    {
        SimMode.Edit => "Edição",
        SimMode.Run => "Rodando",
        SimMode.Pause => "Pausado",
        SimMode.Step => "Passo",
        _ => mode.ToString(),
    };

    private static string DriverText(DriverState state) => state switch
    {
        DriverState.Stopped => "Desconectado",
        DriverState.Starting => "Aguardando master",
        DriverState.Ready => "Conectado",
        DriverState.Faulted => "Erro",
        _ => state.ToString(),
    };
}
