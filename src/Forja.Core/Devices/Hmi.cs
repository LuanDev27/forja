using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>Botão push momentâneo (RF-03 HMI): true enquanto pressionado.</summary>
public sealed class PushButton : DeviceBehavior
{
    private bool _held;

    public override void OnHmi(string portName, bool value)
    {
        if (portName == "pressed")
            _held = value;
    }

    public override void Build(SimContext ctx) => _held = false;

    public override void Tick(SimContext ctx) => ctx.Io.SetInput(Id, "pressed", _held);

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_held);
}

/// <summary>Chave seletora (RF-03 HMI): mantém posição até nova interação.</summary>
public sealed class SelectorSwitch : DeviceBehavior
{
    private bool _on;

    public override void OnHmi(string portName, bool value)
    {
        if (portName == "on")
            _on = value;
    }

    public override void Tick(SimContext ctx) => ctx.Io.SetInput(Id, "on", _on);

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_on);
}

/// <summary>Luz indicadora (RF-03 HMI): reflete o bit de saída "on".</summary>
public sealed class IndicatorLight : DeviceBehavior
{
    /// <summary>Lido pela camada de apresentação para acender o visual.</summary>
    public bool On { get; private set; }

    public override void Tick(SimContext ctx) => On = ctx.Io.GetOutput(Id, "on");

    public override void WriteState(ref StateHasher hasher) => hasher.Add(On);
}
