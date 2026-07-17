using Forja.Anvil;
using Forja.Core.Physics;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>Tipo de peça (RF-03): caixa S/M/L, metal ou plástico.</summary>
public sealed record PartKind(string Size, string Material)
{
    /// <summary>Meia-extensão da caixa em metros por tamanho.</summary>
    public Vec3 HalfExtents => Size switch
    {
        "S" => new Vec3(0.10f, 0.10f, 0.10f),
        "M" => new Vec3(0.15f, 0.15f, 0.15f),
        "L" => new Vec3(0.15f, 0.275f, 0.15f), // mais alta — sensor de altura separa
        _ => new Vec3(0.10f, 0.10f, 0.10f),
    };

    public float Mass => (Material == "metal" ? 3f : 1f) * (Size switch
    {
        "S" => 0.5f,
        "M" => 1f,
        "L" => 1.8f,
        _ => 0.5f,
    });
}

public sealed class Part
{
    public required uint Id { get; init; }
    public required PartKind Kind { get; init; }
    public required IPhysicsBody Body { get; init; }
}

/// <summary>
/// Peças de runtime (data-model §6): ids determinísticos a partir de
/// 1_000_000, iteração em ordem crescente (Artigo I.3), kill-zone destrói
/// peça fora dos limites do mundo sem vazamento (RF-04).
/// </summary>
public sealed class PartsManager
{
    public const uint FirstPartId = 1_000_000;
    private const float WorldLimitXz = 50f;
    private const float WorldFloorY = -5f;

    private readonly SortedDictionary<uint, Part> _parts = new();
    private readonly IPhysicsWorld _physics;
    private uint _nextId = FirstPartId;

    public PartsManager(IPhysicsWorld physics) => _physics = physics;

    public int Count => _parts.Count;

    public IEnumerable<Part> All => _parts.Values;

    public Part SpawnBox(PartKind kind, Pose pose)
    {
        uint id = _nextId++;
        var body = _physics.CreateBox(id, new BodySpec(
            BodyKind.Rigid,
            kind.HalfExtents,
            pose,
            Mass: kind.Mass,
            Friction: 0.7f));
        var part = new Part { Id = id, Kind = kind, Body = body };
        _parts.Add(id, part);
        return part;
    }

    public bool TryGet(uint id, out Part part) => _parts.TryGetValue(id, out part!);

    public bool IsPart(uint entityId) => _parts.ContainsKey(entityId);

    public void Remove(uint id)
    {
        if (_parts.Remove(id, out var part))
            _physics.Remove(part.Body);
    }

    /// <summary>Kill-zone: peça fora dos limites é destruída (RF-04).</summary>
    public void Tick()
    {
        List<uint>? toRemove = null;
        foreach (var part in _parts.Values)
        {
            var p = part.Body.Pose.Pos;
            if (p.Y < WorldFloorY || MathF.Abs(p.X) > WorldLimitXz || MathF.Abs(p.Z) > WorldLimitXz)
                (toRemove ??= new List<uint>()).Add(part.Id);
        }
        if (toRemove is not null)
        {
            foreach (uint id in toRemove)
                Remove(id);
        }
    }

    public void Clear()
    {
        foreach (var part in _parts.Values)
            _physics.Remove(part.Body);
        _parts.Clear();
        _nextId = FirstPartId;
    }

    public void WriteState(ref StateHasher hasher)
    {
        foreach (var part in _parts.Values)
        {
            hasher.Add(part.Id);
            hasher.Add(part.Kind.Size);
            hasher.Add(part.Kind.Material);
            hasher.AddQuantized(part.Body.Pose.Pos);
            hasher.AddQuantized(part.Body.Pose.RotY, 100f);
            hasher.AddQuantized(part.Body.LinearVelocity);
        }
    }
}
