using System.Text;
using Forja.Anvil;

namespace Forja.Core.State;

/// <summary>
/// Hash FNV-1a 64-bit incremental do estado canônico da simulação
/// (Artigo I.4 / RNF-03). Floats entram QUANTIZADOS (research R5):
/// posição em mm, ângulo em mrad, velocidade em mm/s — remove ruído de
/// representação mantendo sensibilidade a divergência real.
/// </summary>
public struct StateHasher
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private ulong _hash;

    public static StateHasher Create() => new() { _hash = FnvOffset };

    public readonly ulong Hash => _hash;

    public void Add(byte b)
    {
        _hash ^= b;
        _hash *= FnvPrime;
    }

    public void Add(ulong value)
    {
        for (int i = 0; i < 8; i++)
            Add((byte)(value >> (i * 8)));
    }

    public void Add(uint value) => Add((ulong)value);

    public void Add(int value) => Add(unchecked((ulong)value));

    public void Add(ushort value)
    {
        Add((byte)value);
        Add((byte)(value >> 8));
    }

    public void Add(bool value) => Add(value ? (byte)1 : (byte)0);

    public void Add(string value)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(value))
            Add(b);
    }

    /// <summary>Float quantizado: scale 1000 = milímetros/milirradianos.</summary>
    public void AddQuantized(float value, float scale = 1000f) =>
        Add(unchecked((ulong)(long)MathF.Round(value * scale)));

    public void AddQuantized(Vec3 v, float scale = 1000f)
    {
        AddQuantized(v.X, scale);
        AddQuantized(v.Y, scale);
        AddQuantized(v.Z, scale);
    }
}
