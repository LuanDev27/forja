namespace Forja.Anvil.Contracts;

/// <summary>
/// Fonte de aleatoriedade injetada e seedada por cena (Artigo I.2).
/// É proibido usar System.Random/GD.Randf no núcleo de simulação.
/// </summary>
public interface IRandomSource
{
    /// <summary>Próximo valor bruto de 64 bits (determinístico para a mesma seed).</summary>
    ulong NextULong();

    /// <summary>Float uniforme em [0, 1).</summary>
    float NextFloat01();

    /// <summary>Inteiro uniforme em [minInclusive, maxExclusive).</summary>
    int NextRange(int minInclusive, int maxExclusive);
}
