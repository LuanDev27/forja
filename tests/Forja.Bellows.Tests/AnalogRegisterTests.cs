using System.Net;
using System.Net.Sockets;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Bellows.Modbus;
using NModbus;
using Xunit;

namespace Forja.Bellows.Tests;

/// <summary>
/// Fase 2 (spec 003 T020): o canal de palavras do modo servidor com um master
/// NModbus real em loopback. Input registers = sensores→master; holding
/// registers = master→atuadores (contrato iosnapshot-words W2/W5).
/// </summary>
public class AnalogRegisterTests
{
    private static ushort FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        ushort port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static ConnectionConfig Config(ushort port) => new()
    {
        Driver = ConnectionConfig.ModbusTcpServerKey,
        BindAddress = "127.0.0.1",
        Port = port,
        UnitId = 1,
        TimeoutMs = 2000,
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

    private static IoSnapshot InputWith(ulong tick, ushort[] words) =>
        new(tick, ReadOnlyMemory<bool>.Empty, words);

    [Fact]
    public void PalavraDeSensor_VisivelAoMaster_NoInputRegister()
    {
        ushort port = FreePort();
        using var driver = new ModbusTcpServerDriver(outputCount: 1, outputWordCount: 1);
        driver.Start(Config(port));
        var (client, master) = ConnectMaster(port);
        using (client)
        using (master)
        {
            master.ReadInputRegisters(1, 0, 1); // aquece a conexão

            // Sensor publica bruto no fim do tick.
            driver.Exchange(InputWith(1, new ushort[] { 12345 }));

            ushort[] regs = master.ReadInputRegisters(1, 0, 1);
            Assert.Equal(12345, regs[0]);
        }
    }

    [Fact]
    public void SetpointDoMaster_ChegaNoSnapshotSeguinte_NoHoldingRegister()
    {
        ushort port = FreePort();
        using var driver = new ModbusTcpServerDriver(outputCount: 1, outputWordCount: 2);
        driver.Start(Config(port));
        var (client, master) = ConnectMaster(port);
        using (client)
        using (master)
        {
            master.WriteSingleRegister(1, 0, 40000);
            master.WriteMultipleRegisters(1, 1, new ushort[] { 25000 });

            var outputs = driver.Exchange(IoSnapshot.Empty(1));

            Assert.True(outputs.Valid);
            Assert.Equal(40000, outputs.Words.Span[0]);
            Assert.Equal(25000, outputs.Words.Span[1]);
        }
    }

    [Fact]
    public void FalhaDoMaster_IgnoraPalavrasVelhas_SnapshotInvalido()
    {
        // Sem master conectado (Stopped→Exchange), o snapshot é inválido e as
        // palavras não devem ser aplicadas rio acima (contrato W4).
        using var driver = new ModbusTcpServerDriver(outputCount: 1, outputWordCount: 1);
        var outputs = driver.Exchange(IoSnapshot.Empty(1));
        Assert.False(outputs.Valid);
    }
}
