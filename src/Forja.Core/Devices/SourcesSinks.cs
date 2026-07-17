using Forja.Anvil;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>
/// Emissor de peças (RF-03): intervalo fixo (decisão Q3), tipo e quantidade
/// máxima configuráveis. Aleatoriedade só via IRandomSource (Artigo I.2).
/// </summary>
public sealed class Emitter : DeviceBehavior
{
    private int _ticksSinceLast;
    private int _spawned;

    public override void Build(SimContext ctx)
    {
        _ticksSinceLast = 0;
        _spawned = 0;
    }

    public override void Tick(SimContext ctx)
    {
        int intervalTicks = Math.Max(1, (int)MathF.Round(GetFloat("interval", 2f) * 60f));
        int maxParts = GetInt("maxParts", 0);

        _ticksSinceLast++;
        if (_ticksSinceLast < intervalTicks)
            return;
        if (maxParts > 0 && _spawned >= maxParts)
            return;

        _ticksSinceLast = 0;
        _spawned++;

        var sizes = GetString("sizes", "S").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string size = sizes.Length == 1 ? sizes[0] : sizes[ctx.Random.NextRange(0, sizes.Length)];

        string material = GetString("material", "plastic");
        if (material == "mix")
            material = ctx.Random.NextRange(0, 2) == 0 ? "plastic" : "metal";

        ctx.Parts.SpawnBox(new PartKind(size, material), Instance.Transform);
    }

    public override void WriteState(ref StateHasher hasher)
    {
        hasher.Add(_ticksSinceLast);
        hasher.Add(_spawned);
    }
}

/// <summary>Removedor de peças (sink): destrói o que entra na sua região.</summary>
public sealed class Sink : DeviceBehavior
{
    public override void Tick(SimContext ctx)
    {
        var half = new Vec3(GetFloat("sizeX", 0.6f) / 2f, GetFloat("sizeY", 0.6f) / 2f, GetFloat("sizeZ", 0.6f) / 2f);
        var ids = ctx.Physics.QueryBox(Instance.Transform.Pos, half);

        List<uint>? parts = null;
        foreach (uint id in ids)
        {
            if (ctx.Parts.IsPart(id))
                (parts ??= new List<uint>()).Add(id);
        }
        if (parts is null)
            return;

        // Ordena para remoção determinística (a engine não garante ordem).
        parts.Sort();
        foreach (uint id in parts)
            ctx.Parts.Remove(id);
    }
}
