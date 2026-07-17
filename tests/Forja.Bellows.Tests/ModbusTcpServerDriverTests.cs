using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Bellows.Modbus;
using NModbus;
using Xunit;

namespace Forja.Bellows.Tests;

/// <summary>
/// T048 — testes de contrato do modo servidor com um master NModbus real em
/// loopback (sem PLC físico, Artigo V.1).
/// </summary>
public class ModbusTcpServerDriverTests
{
    private static ushort FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        ushort port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static ConnectionConfig Config(ushort port, int timeoutMs = 2000) => new()
    {
        Driver = ConnectionConfig.ModbusTcpServerKey,
        BindAddress = "127.0.0.1",
        Port = port,
        UnitId = 1,
        TimeoutMs = timeoutMs,
    };

    private static (TcpClient Client, IModbusMaster Master) ConnectMaster(ushort port)
    {
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var master = new ModbusFactory().CreateMaster(client);
        master.Transport.ReadTimeout = 2000;
        master.Transport.WriteTimeout = 2000;
        return (client, master);
    }

    [Fact]
    public void SensorVisivelAoMaster_EmMenosDe20ms()
    {
        ushort port = FreePort();
        using var driver = new ModbusTcpServerDriver(outputCount: 8);
        driver.Start(Config(port));
        var (client, master) = ConnectMaster(port);
        using (client)
        using (master)
        {
            master.ReadInputs(1, 0, 1); // aquece a conexão

            // Sensor liga no fim do tick → publicação imediata (RNF-02).
            driver.Exchange(new IoSnapshot(1, new[] { true }));

            var sw = Stopwatch.StartNew();
            bool[] inputs = master.ReadInputs(1, 0, 1);
            sw.Stop();

            Assert.True(inputs[0]);
            Assert.True(sw.ElapsedMilliseconds < 20,
                $"FC02 em loopback levou {sw.ElapsedMilliseconds} ms (alvo < 20 ms — RNF-02).");
        }
    }

    [Fact]
    public void EscritaDeCoilPeloMaster_ChegaNoSnapshotSeguinte()
    {
        ushort port = FreePort();
        using var driver = new ModbusTcpServerDriver(outputCount: 8);
        driver.Start(Config(port));
        var (client, master) = ConnectMaster(port);
        using (client)
        using (master)
        {
            master.WriteSingleCoil(1, 1, true);
            master.WriteMultipleCoils(1, 4, new[] { true, true });

            var outputs = driver.Exchange(IoSnapshot.Empty(1));

            Assert.True(outputs.Valid);
            Assert.False(outputs.Bits.Span[0]);
            Assert.True(outputs.Bits.Span[1]);
            Assert.True(outputs.Bits.Span[4]);
            Assert.True(outputs.Bits.Span[5]);
        }
    }

    [Fact]
    public void MasterConectado_DriverFicaReady()
    {
        ushort port = FreePort();
        using var driver = new ModbusTcpServerDriver(outputCount: 8);
        driver.Start(Config(port));

        Assert.Equal(DriverState.Starting, driver.State); // aguardando master

        var (client, master) = ConnectMaster(port);
        using (client)
        using (master)
        {
            master.ReadInputs(1, 0, 1);
            driver.Exchange(IoSnapshot.Empty(1));

            Assert.Equal(DriverState.Ready, driver.State);
        }
    }

    [Fact]
    public void MasterDesconecta_FaultedComMotivo_ESnapshotInvalido()
    {
        ushort port = FreePort();
        using var driver = new ModbusTcpServerDriver(outputCount: 8);
        driver.Start(Config(port, timeoutMs: 150));

        var faults = new List<string?>();
        driver.StateChanged += (state, reason) =>
        {
            if (state == DriverState.Faulted)
                faults.Add(reason);
        };

        var (client, master) = ConnectMaster(port);
        master.ReadInputs(1, 0, 1);
        driver.Exchange(IoSnapshot.Empty(1));
        Assert.Equal(DriverState.Ready, driver.State);

        master.Dispose();
        client.Dispose(); // "cabo arrancado"
        Thread.Sleep(300); // > timeout de 150 ms sem atividade

        var outputs = driver.Exchange(IoSnapshot.Empty(2));

        Assert.Equal(DriverState.Faulted, driver.State);
        Assert.False(outputs.Valid); // C1: snapshot inválido, nunca exceção
        string? reason = Assert.Single(faults);
        Assert.False(string.IsNullOrWhiteSpace(reason)); // motivo obrigatório (VII.3)
    }

    [Fact]
    public void StartStop_Idempotentes_EPortaOcupadaEhFaultedExplicito()
    {
        ushort port = FreePort();
        using var driver = new ModbusTcpServerDriver();
        driver.Stop(); // Stop sem Start não lança (C4)
        driver.Start(Config(port));
        driver.Start(Config(port)); // segundo Start é no-op

        using var driver2 = new ModbusTcpServerDriver();
        string? reason = null;
        driver2.StateChanged += (state, r) =>
        {
            if (state == DriverState.Faulted)
                reason = r;
        };
        driver2.Start(Config(port)); // mesma porta → Faulted com motivo

        Assert.Equal(DriverState.Faulted, driver2.State);
        Assert.Contains(port.ToString(), reason);

        driver.Stop();
        driver.Stop(); // idempotente
        Assert.Equal(DriverState.Stopped, driver.State);
    }
}
