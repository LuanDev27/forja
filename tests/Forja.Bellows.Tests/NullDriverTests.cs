using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Bellows;
using Forja.Bellows.Null;
using Xunit;

namespace Forja.Bellows.Tests;

public class NullDriverTests
{
    [Fact]
    public void CoilForcada_VoltaNoExchange()
    {
        using var driver = new NullDriver(outputCount: 8);
        driver.Start(new ConnectionConfig());

        driver.SetCoil(3, true);
        var outputs = driver.Exchange(new IoSnapshot(1, new[] { true, false }));

        Assert.True(outputs.Valid);
        Assert.True(outputs.Bits.Span[3]);
        Assert.False(outputs.Bits.Span[0]);
        Assert.True(driver.LastInputs.Bits.Span[0]);
    }

    [Fact]
    public void NuncaFaulted()
    {
        using var driver = new NullDriver();
        var states = new List<DriverState>();
        driver.StateChanged += (state, _) => states.Add(state);

        driver.Start(new ConnectionConfig());
        driver.Exchange(IoSnapshot.Empty(1));
        driver.Stop();

        Assert.DoesNotContain(DriverState.Faulted, states);
    }

    [Fact]
    public void StartStop_Idempotentes()
    {
        using var driver = new NullDriver();

        driver.Stop(); // Stop sem Start não lança (C4)

        int changes = 0;
        driver.StateChanged += (_, _) => changes++;
        driver.Start(new ConnectionConfig());
        driver.Start(new ConnectionConfig()); // segundo Start é no-op

        Assert.Equal(DriverState.Ready, driver.State);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Dispose_EquivaleAStop()
    {
        var driver = new NullDriver();
        driver.Start(new ConnectionConfig());

        driver.Dispose();

        Assert.Equal(DriverState.Stopped, driver.State);
    }
}

public class DriverRegistryTests
{
    [Fact]
    public void ChavesConhecidas_ResolvemDriver()
    {
        using var nullDriver = DriverRegistry.Create(
            new ConnectionConfig { Driver = ConnectionConfig.NullDriverKey }, 4);
        using var server = DriverRegistry.Create(
            new ConnectionConfig { Driver = ConnectionConfig.ModbusTcpServerKey }, 4);
        using var client = DriverRegistry.Create(
            new ConnectionConfig { Driver = ConnectionConfig.ModbusTcpClientKey }, 4);

        Assert.IsType<NullDriver>(nullDriver);
        Assert.IsType<Modbus.ModbusTcpServerDriver>(server);
        Assert.IsType<Modbus.ModbusTcpClientDriver>(client);
    }

    [Fact]
    public void ChaveDesconhecida_FalhaAlto()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DriverRegistry.Create(new ConnectionConfig { Driver = "profinet" }, 4));

        Assert.Contains("profinet", ex.Message);
    }
}
