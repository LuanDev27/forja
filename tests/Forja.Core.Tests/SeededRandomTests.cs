using Forja.Core.State;
using Xunit;

namespace Forja.Core.Tests;

public class SeededRandomTests
{
    [Fact]
    public void MesmaSeed_MesmaSequencia()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void SeedsDiferentes_SequenciasDiferentes()
    {
        var a = new SeededRandom(1);
        var b = new SeededRandom(2);

        Assert.NotEqual(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void NextRange_RespeitaLimites()
    {
        var rng = new SeededRandom(7);
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.NextRange(3, 9);
            Assert.InRange(v, 3, 8);
        }
    }

    [Fact]
    public void NextFloat01_Em01()
    {
        var rng = new SeededRandom(7);
        for (int i = 0; i < 1000; i++)
        {
            float f = rng.NextFloat01();
            Assert.InRange(f, 0f, 1f);
        }
    }

    [Fact]
    public void RangeInvalido_Lanca()
    {
        var rng = new SeededRandom(7);
        Assert.Throws<ArgumentException>(() => rng.NextRange(5, 5));
    }
}
