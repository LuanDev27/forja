using System.Net;
using System.Net.Sockets;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Bellows.Modbus;
using NModbus;
using Xunit;

namespace Forja.Bellows.Tests;

/// <summary>
/// T049b — modo cliente (Forja master) contra um servidor NModbus loopback:
/// FC15 leva sensores às coils do PLC, FC01 traz atuadores de volta.
/// </summary>
public class ModbusTcpClientDriverTests : IDisposable
{
    private readonly TcpListener _listener;
    private readonly IModbusSlaveNetwork _network;
    private readonly IModbusSlave _slave;
    private readonly CancellationTokenSource _cts = new();
    private readonly ushort _port;

    public ModbusTcpClientDriverTests()
    {
        var factory = new ModbusFactory();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _network = factory.CreateSlaveNetwork(_listener);
        _slave = factory.CreateSlave(1);
        _network.AddSlave(_slave);
        _listener.Start();
        _port = (ushort)((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = _network.ListenAsync(_cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _network.Dispose();
        _listener.Stop();
        GC.SuppressFinalize(this);
    }

    private ConnectionConfig Config(int timeoutMs = 2000) => new()
    {
        Driver = ConnectionConfig.ModbusTcpClientKey,
        Host = "127.0.0.1",
        Port = _port,
        UnitId = 1,
        TimeoutMs = timeoutMs,
    };

    private static void WaitReady(ModbusTcpClientDriver driver)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (driver.State != DriverState.Ready && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
        Assert.Equal(DriverState.Ready, driver.State);
    }

    [Fact]
    public void Conecta_EscreveSensoresELeAtuadores()
    {
        // Programa do "PLC": coil 5 ligada (atuador da Forja).
        _slave.DataStore.CoilDiscretes.WritePoints(5, new[] { true });

        using var driver = new ModbusTcpClientDriver(outputCount: 8);
        driver.Start(Config());
        WaitReady(driver);

        var outputs = driver.Exchange(new IoSnapshot(1, new[] { true, false, true }));

        Assert.True(outputs.Valid);
        Assert.True(outputs.Bits.Span[5]); // FC01 trouxe a coil do PLC

        // FC15 levou os sensores às coils 0..2 do PLC.
        bool[] written = _slave.DataStore.CoilDiscretes.ReadPoints(0, 3);
        Assert.True(written[0]);
        Assert.False(written[1]);
        Assert.True(written[2]);
    }

    [Fact]
    public void QuedaDoServidor_FaultedComMotivo_ESnapshotInvalido()
    {
        using var driver = new ModbusTcpClientDriver(outputCount: 4);
        driver.Start(Config(timeoutMs: 500));
        WaitReady(driver);
        Assert.True(driver.Exchange(new IoSnapshot(1, new[] { true })).Valid);

        var faults = new List<string?>();
        driver.StateChanged += (state, reason) =>
        {
            if (state == DriverState.Faulted)
                faults.Add(reason);
        };

        _cts.Cancel();
        _network.Dispose();
        _listener.Stop(); // PLC caiu

        // C1: nunca lança — em poucas trocas o socket morto vira Faulted.
        IoSnapshot last = default;
        for (int i = 0; i < 50 && driver.State != DriverState.Faulted; i++)
        {
            last = driver.Exchange(new IoSnapshot((ulong)(2 + i), new[] { true }));
            Thread.Sleep(50);
        }

        Assert.Equal(DriverState.Faulted, driver.State);
        Assert.False(last.Valid);
        Assert.NotEmpty(faults);
        Assert.False(string.IsNullOrWhiteSpace(faults[0]));
    }

    [Fact]
    public void InputBaseOffset_SeparaJanelaDeEscritaDasCoilsDosAtuadores()
    {
        // Atuador da Forja: coil 1 do PLC ligada.
        _slave.DataStore.CoilDiscretes.WritePoints(1, new[] { true });

        using var driver = new ModbusTcpClientDriver(outputCount: 8);
        driver.Start(Config() with { InputBaseOffset = 100 });
        WaitReady(driver);

        var outputs = driver.Exchange(new IoSnapshot(1, new[] { true, false, true }));

        // FC15 foi para 100..102 — as coils baixas ficaram intactas.
        bool[] high = _slave.DataStore.CoilDiscretes.ReadPoints(100, 3);
        Assert.True(high[0]);
        Assert.False(high[1]);
        Assert.True(high[2]);

        bool[] low = _slave.DataStore.CoilDiscretes.ReadPoints(0, 3);
        Assert.False(low[0]);
        Assert.True(low[1]); // atuador preservado e lido de volta
        Assert.False(low[2]);
        Assert.True(outputs.Valid);
        Assert.True(outputs.Bits.Span[1]);
    }

    [Fact]
    public void StartStop_Idempotentes()
    {
        using var driver = new ModbusTcpClientDriver(outputCount: 4);
        driver.Stop(); // Stop sem Start não lança (C4)
        driver.Start(Config());
        driver.Start(Config()); // no-op
        WaitReady(driver);
        driver.Stop();
        driver.Stop();

        Assert.Equal(DriverState.Stopped, driver.State);
    }
}
