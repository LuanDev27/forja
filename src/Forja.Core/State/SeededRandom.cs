using Forja.Anvil.Contracts;

namespace Forja.Core.State;

/// <summary>
/// PRNG determinístico (splitmix64) seedado por cena (Artigo I.2).
/// Só operações inteiras — mesmo resultado em qualquer plataforma.
/// </summary>
public sealed class SeededRandom : IRandomSource
{
    private ulong _state;

    public SeededRandom(ulong seed) => _state = seed;

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public float NextFloat01() =>
        (NextULong() >> 40) * (1.0f / (1 << 24));

    public int NextRange(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentException("maxExclusive deve ser > minInclusive.");
        ulong range = (ulong)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextULong() % range);
    }
}
