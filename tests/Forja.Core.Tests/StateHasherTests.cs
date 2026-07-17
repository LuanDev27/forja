using Forja.Anvil;
using Forja.Core.State;
using Xunit;

namespace Forja.Core.Tests;

public class StateHasherTests
{
    [Fact]
    public void MesmaSequencia_MesmoHash()
    {
        var a = StateHasher.Create();
        a.Add(42UL);
        a.Add(true);
        a.Add("esteira");
        a.AddQuantized(new Vec3(1f, 2f, 3f));

        var b = StateHasher.Create();
        b.Add(42UL);
        b.Add(true);
        b.Add("esteira");
        b.AddQuantized(new Vec3(1f, 2f, 3f));

        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void OrdemImporta()
    {
        var a = StateHasher.Create();
        a.Add(1UL);
        a.Add(2UL);

        var b = StateHasher.Create();
        b.Add(2UL);
        b.Add(1UL);

        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void RuidoSubMilimetrico_NaoMudaHash()
    {
        // Quantização em mm (research R5): diferença de 0,0004 mm é ruído de
        // representação, não divergência real.
        var a = StateHasher.Create();
        a.AddQuantized(1.0f);

        var b = StateHasher.Create();
        b.AddQuantized(1.0000004f);

        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void DivergenciaReal_MudaHash()
    {
        var a = StateHasher.Create();
        a.AddQuantized(1.000f);

        var b = StateHasher.Create();
        b.AddQuantized(1.002f); // 2 mm

        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void BoolsDistintos_HashesDistintos()
    {
        var a = StateHasher.Create();
        a.Add(true);

        var b = StateHasher.Create();
        b.Add(false);

        Assert.NotEqual(a.Hash, b.Hash);
    }
}
