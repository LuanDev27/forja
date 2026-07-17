using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Io;
using Xunit;

namespace Forja.Core.Tests;

public class IoTableTests
{
    private static IoTable Table() => new(TestWorld.SensorAtuadorDoc(), TestWorld.Catalog());

    [Fact]
    public void SensorEscreve_SnapshotDeEntradaReflete()
    {
        var io = Table();

        io.SetInput(1, "detect", true);
        var snapshot = io.BuildInputSnapshot(10);

        Assert.True(snapshot.Valid);
        Assert.Equal(10UL, snapshot.TickNumber);
        Assert.True(snapshot.Bits.Span[0]);
    }

    [Fact]
    public void SaidaDoDriver_ChegaNoAtuador()
    {
        var io = Table();

        io.ApplyOutputSnapshot(new IoSnapshot(1, new[] { true }));

        Assert.True(io.GetOutput(2, "extend"));
    }

    [Fact]
    public void SnapshotInvalido_EIgnorado()
    {
        var io = Table();
        io.ApplyOutputSnapshot(new IoSnapshot(1, new[] { true }));

        // Falha do driver (C1): snapshot inválido não sobrescreve nada.
        io.ApplyOutputSnapshot(new IoSnapshot(2, new[] { false }, Valid: false));

        Assert.True(io.GetOutput(2, "extend"));
    }

    [Fact]
    public void ForcarSaida_PrevaleceSobreDriver_AteLiberar()
    {
        var io = Table();
        var coil0 = new IoAddress(IoArea.Coil, 0);

        io.Force(coil0, true);
        io.ApplyOutputSnapshot(new IoSnapshot(1, new[] { false }));
        Assert.True(io.GetOutput(2, "extend"));

        io.Force(coil0, null); // libera
        Assert.False(io.GetOutput(2, "extend"));
    }

    [Fact]
    public void ForcarEntrada_PrevaleceSobreSensor()
    {
        var io = Table();
        var di0 = new IoAddress(IoArea.DiscreteInput, 0);

        io.SetInput(1, "detect", false);
        io.Force(di0, true);

        Assert.True(io.BuildInputSnapshot(1).Bits.Span[0]);
    }

    [Fact]
    public void View_MostraValorDirecaoEForcado()
    {
        var io = Table();
        io.Force(new IoAddress(IoArea.Coil, 0), true);

        var rows = io.BuildView();

        Assert.Equal(2, rows.Count);
        var coil = Assert.Single(rows, r => r.Direction == IoDirection.Out);
        Assert.True(coil.Forced);
        Assert.True(coil.Value);
        Assert.Equal("%QX0.0 (Coil 0)", coil.Address.ToDisplay());
    }

    [Fact]
    public void Reset_LimpaValoresEOverrides()
    {
        var io = Table();
        io.SetInput(1, "detect", true);
        io.Force(new IoAddress(IoArea.Coil, 0), true);

        io.Reset();

        Assert.False(io.BuildInputSnapshot(1).Bits.Span[0]);
        Assert.False(io.GetOutput(2, "extend"));
        Assert.DoesNotContain(io.BuildView(), r => r.Forced);
    }
}
