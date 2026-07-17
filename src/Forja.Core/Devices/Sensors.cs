using Forja.Anvil;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>
/// Sensor fotoelétrico de barreira (RF-03): feixe por raycast ao longo do
/// eixo local +X. Detecta apenas peças — estrutura não interrompe.
/// Também usado pelo sensor de altura/difuso (posicionado na altura desejada).
/// </summary>
public sealed class PhotoSensor : DeviceBehavior
{
    private bool _detected;

    public override void Tick(SimContext ctx)
    {
        var from = Instance.Transform.Pos;
        var to = from + LocalXAxis() * GetFloat("range", 1f);

        var hit = ctx.Physics.Raycast(from, to);
        _detected = hit is { } h && ctx.Parts.IsPart(h.EntityId);

        ctx.Io.SetInput(Id, "detect", _detected);
    }

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_detected);
}

/// <summary>
/// Sensor de proximidade (RF-03): capacitivo detecta qualquer peça;
/// indutivo detecta só metal (usa PartKind.Material).
/// </summary>
public sealed class ProximitySensor : DeviceBehavior
{
    private bool _detected;

    public override void Tick(SimContext ctx)
    {
        float range = GetFloat("range", 0.3f);
        bool inductiveOnly = GetString("mode", "capacitive") == "inductive";

        var center = Instance.Transform.Pos + LocalXAxis() * (range / 2f);
        var ids = ctx.Physics.QueryBox(center, new Vec3(range / 2f, 0.15f, 0.15f));

        _detected = false;
        foreach (uint id in ids)
        {
            if (!ctx.Parts.TryGet(id, out var part))
                continue;
            if (!inductiveOnly || part.Kind.Material == "metal")
            {
                _detected = true;
                break;
            }
        }

        ctx.Io.SetInput(Id, "detect", _detected);
    }

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_detected);
}
