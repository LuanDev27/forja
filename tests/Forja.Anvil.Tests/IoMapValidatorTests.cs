using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Xunit;

namespace Forja.Anvil.Tests;

public class IoMapValidatorTests
{
    private static SceneDocument Doc(
        List<DeviceInstance> devices,
        List<IoTag> ioMap,
        ConnectionConfig? connection = null) => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "teste",
        Devices = devices,
        IoMap = ioMap,
        Connection = connection ?? new ConnectionConfig(),
    };

    [Fact]
    public void MapaValido_SemErros()
    {
        var doc = Doc(
            new() { TestCatalog.Sensor(1), TestCatalog.Actuator(2) },
            new() { TestCatalog.Di(1, 0), TestCatalog.Coil(2, 0) });

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Empty(errors);
    }

    [Fact]
    public void PortaIn_EmCoil_ModoServidor_Erro()
    {
        var doc = Doc(
            new() { TestCatalog.Sensor(1) },
            new() { new IoTag(1, "detect", new IoAddress(IoArea.Coil, 0)) });

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Contains(errors, e => e.Code == "type-area-mismatch" && e.DeviceIds.Contains(1u));
    }

    [Fact]
    public void PortaIn_EmCoil_ModoCliente_Aceita()
    {
        // Limite do protocolo: cliente não escreve DI remoto — In mapeia em
        // coil do PLC (contracts/modbus-mapping.md).
        var doc = Doc(
            new() { TestCatalog.Sensor(1) },
            new() { new IoTag(1, "detect", new IoAddress(IoArea.Coil, 0)) },
            new ConnectionConfig { Driver = ConnectionConfig.ModbusTcpClientKey });

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Empty(errors);
    }

    [Fact]
    public void PortaBool_EmRegister_Erro()
    {
        // Fase 2 (FR-011/FR-012): a regra digital-only virou matriz
        // direção×área×tipo. Uma porta Bool ("detect") num register é
        // incoerência de tipo — erro, não warning.
        var doc = Doc(
            new() { TestCatalog.Sensor(1) },
            new() { new IoTag(1, "detect", new IoAddress(IoArea.InputRegister, 0)) });

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Contains(errors, e => e.Code == "type-area-mismatch");
    }

    /// <summary>
    /// Aceite V-D do quickstart (RF-05, Artigo VI.3): dois dispositivos no
    /// mesmo endereço barram o Run, e a mensagem precisa citar OS DOIS — de
    /// nada adianta apontar só um deles para quem vai corrigir o mapa.
    /// </summary>
    [Fact]
    public void EnderecoDuplicado_ErroCitaOsDoisDispositivos()
    {
        var doc = Doc(
            new() { TestCatalog.Sensor(1), TestCatalog.Sensor(2) },
            new() { TestCatalog.Di(1, 4), TestCatalog.Di(2, 4) });

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        var error = Assert.Single(errors, e => e.Code == "duplicate-address");
        Assert.Contains(1u, error.DeviceIds);
        Assert.Contains(2u, error.DeviceIds);
        Assert.Contains("%IX0.4", error.Message);
    }

    [Fact]
    public void TagOrfa_Erro()
    {
        var doc = Doc(new(), new() { TestCatalog.Di(99, 0) });

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Contains(errors, e => e.Code == "orphan-tag" && e.DeviceIds.Contains(99u));
    }

    [Fact]
    public void PortaSemTag_Erro()
    {
        var doc = Doc(new() { TestCatalog.Sensor(1) }, new());

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Contains(errors, e => e.Code == "missing-tag" && e.DeviceIds.Contains(1u));
    }

    [Fact]
    public void PortaDesconhecida_Erro()
    {
        var doc = Doc(
            new() { TestCatalog.Sensor(1) },
            new()
            {
                TestCatalog.Di(1, 0),
                new IoTag(1, "inexistente", new IoAddress(IoArea.DiscreteInput, 1)),
            });

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Contains(errors, e => e.Code == "unknown-port");
    }

    [Fact]
    public void PassivoSemPortas_NaoExigeTag()
    {
        var doc = Doc(
            new() { new DeviceInstance { Id = 1, TypeId = "passive.test" } },
            new());

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.Empty(errors);
    }
}
