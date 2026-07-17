using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Forja.Core.Devices;
using Forja.Core.Loop;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>Máquina de modos do data-model §8 (RF-01).</summary>
public class SimModeTests
{
    private static (SimulationLoop Loop, FakePhysicsWorld Physics, FakeDriver Driver) Make(
        SceneDocument? doc = null)
    {
        var physics = new FakePhysicsWorld();
        var driver = new FakeDriver();
        var loop = new SimulationLoop(
            doc ?? TestWorld.SensorAtuadorDoc(),
            TestWorld.Catalog(),
            TestWorld.Factory(),
            physics,
            _ => driver);
        return (loop, physics, driver);
    }

    private static void Request(SimulationLoop loop, SimMode target)
    {
        loop.Enqueue(new SetModeCommand(target));
        loop.Tick();
    }

    [Fact]
    public void EditParaRun_ValidaConstroiEStartaDriver()
    {
        var (loop, physics, driver) = Make();

        Request(loop, SimMode.Run);

        Assert.Equal(SimMode.Run, loop.Mode);
        Assert.True(physics.Active);
        Assert.Equal(1, driver.StartCalls);
        Assert.True(loop.TickNumber >= 1); // já tickou no mesmo frame
    }

    [Fact]
    public void CenaInvalida_BloqueiaRun()
    {
        var doc = TestWorld.Doc(
            new()
            {
                new DeviceInstance { Id = 1, TypeId = "sensor.test" },
                new DeviceInstance { Id = 2, TypeId = "sensor.test" },
            },
            new()
            {
                new IoTag(1, "detect", new IoAddress(IoArea.DiscreteInput, 0)),
                new IoTag(2, "detect", new IoAddress(IoArea.DiscreteInput, 0)),
            });
        var (loop, _, driver) = Make(doc);

        IReadOnlyList<ValidationError>? reported = null;
        loop.ValidationFailed += errors => reported = errors;

        Request(loop, SimMode.Run);

        Assert.Equal(SimMode.Edit, loop.Mode);
        Assert.Equal(0, driver.StartCalls);
        Assert.NotNull(reported);
        Assert.Contains(reported!, e => e.Code == "duplicate-address");
    }

    [Fact]
    public void Step_AvancaExatamenteUmTickECaiEmPause()
    {
        var (loop, _, _) = Make();
        Request(loop, SimMode.Run);
        Request(loop, SimMode.Pause);
        ulong before = loop.TickNumber;

        Request(loop, SimMode.Step);

        Assert.Equal(before + 1, loop.TickNumber);
        Assert.Equal(SimMode.Pause, loop.Mode);

        // Frames seguintes em Pause não avançam nada.
        loop.Tick();
        loop.Tick();
        Assert.Equal(before + 1, loop.TickNumber);
    }

    [Fact]
    public void PauseRun_NaoDaSaltoDeFisica()
    {
        var (loop, physics, _) = Make();
        Request(loop, SimMode.Run);
        Request(loop, SimMode.Pause);
        Assert.False(physics.Active);

        ulong paused = loop.TickNumber;
        Request(loop, SimMode.Run);

        Assert.True(physics.Active);
        Assert.Equal(paused + 1, loop.TickNumber); // exatamente 1 tick por frame
    }

    [Fact]
    public void TransicaoInvalida_EhIgnorada()
    {
        var (loop, _, _) = Make();

        Request(loop, SimMode.Step); // Edit→Step não existe
        Assert.Equal(SimMode.Edit, loop.Mode);

        Request(loop, SimMode.Pause); // Edit→Pause não existe
        Assert.Equal(SimMode.Edit, loop.Mode);
    }

    [Fact]
    public void VoltarParaEdit_ParaDriverEFisica()
    {
        var (loop, physics, driver) = Make();
        Request(loop, SimMode.Run);

        Request(loop, SimMode.Edit);

        Assert.Equal(SimMode.Edit, loop.Mode);
        Assert.False(physics.Active);
        Assert.True(driver.StopCalls >= 1);
        Assert.Equal(DriverState.Stopped, driver.State);
    }

    [Fact]
    public void DriverFaulted_DerrubaRunParaPause_ComMotivo()
    {
        var (loop, _, driver) = Make();
        Request(loop, SimMode.Run);

        string? fault = null;
        loop.DriverFault += reason => fault = reason;

        driver.RaiseFault("cabo arrancado");
        loop.Tick();

        Assert.Equal(SimMode.Pause, loop.Mode);
        Assert.Equal("cabo arrancado", fault);
    }

    [Fact]
    public void DriverRecuperadoDurantePause_NaoDerrubaProximoRun()
    {
        // Regressão: fault durante Pause + recuperação automática (master
        // reconectou) não pode reter o motivo antigo e pausar o Run seguinte.
        var (loop, _, driver) = Make();
        Request(loop, SimMode.Run);
        Request(loop, SimMode.Pause);

        driver.RaiseFault("queda momentânea");
        driver.RaiseRecovered();

        string? fault = null;
        loop.DriverFault += reason => fault = reason;

        Request(loop, SimMode.Run);
        loop.Tick();

        Assert.Equal(SimMode.Run, loop.Mode);
        Assert.Null(fault);
    }

    [Fact]
    public void ReplaceDocument_SoEmEdit()
    {
        var (loop, _, _) = Make();
        var other = TestWorld.SensorAtuadorDoc();

        Assert.True(loop.ReplaceDocument(other));

        Request(loop, SimMode.Run);
        Assert.False(loop.ReplaceDocument(TestWorld.SensorAtuadorDoc()));
    }

    [Fact]
    public void DoisRuns_MesmaCena_HashesIdenticos()
    {
        // Mini-determinismo (Artigo I.4): 100 ticks, duas execuções.
        static ulong RunHash()
        {
            var (loop, _, _) = Make();
            loop.Enqueue(new SetModeCommand(SimMode.Run));
            for (int i = 0; i < 100; i++)
                loop.Tick();
            return loop.ComputeStateHash();
        }

        Assert.Equal(RunHash(), RunHash());
    }
}
