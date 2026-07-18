using Forja.Anvil;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>
/// Sensor fotoelétrico de barreira (RF-03): feixe por raycast ao longo do
/// eixo local +X. Semântica decidida no T026: é um feixe físico — o raycast
/// para no PRIMEIRO corpo, e só marca detecção se esse corpo for uma peça
/// (estrutura no caminho corta o feixe, como no mundo real). Por isso o
/// sensor é posicionado de modo que só peças cruzem o feixe.
/// Também usado como sensor de altura/difuso (posicionado na altura desejada).
/// </summary>
public sealed class PhotoSensor : DeviceBehavior
{
    private bool _detected;

    /// <summary>Estado atual da detecção — a camada visual acende a lente
    /// com isso (só leitura, Artigo II.2).</summary>
    public bool Detected => _detected;

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
/// Sensor de altura difuso (RF-03): montado ACIMA da esteira olhando para
/// baixo (−Y), mede a distância até o primeiro corpo. Detecta quando o eco
/// vem de uma peça a até "threshold" metros — peça alta chega perto do
/// sensor, peça baixa fica além do threshold. É o sensor do separador por
/// altura da demo (contracts/modbus-mapping.md).
/// </summary>
public sealed class HeightSensor : DeviceBehavior
{
    private bool _detected;

    /// <summary>Estado atual da detecção — a camada visual acende a lente
    /// com isso (só leitura, Artigo II.2).</summary>
    public bool Detected => _detected;

    public override void Tick(SimContext ctx)
    {
        var from = Instance.Transform.Pos;
        var to = from + new Vec3(0f, -GetFloat("range", 2f), 0f);

        var hit = ctx.Physics.Raycast(from, to);
        _detected = hit is { } h && ctx.Parts.IsPart(h.EntityId)
            && from.Y - h.Point.Y <= GetFloat("threshold", 0.5f);

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

    /// <summary>Estado atual da detecção — a camada visual acende a lente
    /// com isso (só leitura, Artigo II.2).</summary>
    public bool Detected => _detected;

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
