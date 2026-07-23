using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Xunit;

namespace Forja.Anvil.Tests;

/// <summary>
/// Fase 2 (spec 003 T018): a matriz direção×área×tipo que substituiu o
/// digital-only, mais a escala degenerada e o conflito de endereço com palavra
/// (contrato scaling-eu-raw V1–V3).
/// </summary>
public class AnalogValidationTests
{
    private static DeviceCatalog Catalog() => DeviceCatalog.FromDefs(new[]
    {
        new DeviceTypeDef
        {
            TypeId = "sensor.level",
            Category = DeviceCategory.Sensor,
            DisplayName = "Sensor de nível",
            Behavior = "noop",
            Ports = new() { new PortDef("level", IoDirection.In, PortType.Word) },
        },
        new DeviceTypeDef
        {
            TypeId = "actuator.vspeed",
            Category = DeviceCategory.Actuator,
            DisplayName = "Esteira VV",
            Behavior = "noop",
            Ports = new() { new PortDef("speed", IoDirection.Out, PortType.Word) },
        },
    }).Require();

    private static SceneDocument Doc(List<IoTag> ioMap, ConnectionConfig? conn = null) => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "teste",
        Devices = new()
        {
            new DeviceInstance { Id = 1, TypeId = "sensor.level" },
            new DeviceInstance { Id = 2, TypeId = "actuator.vspeed" },
        },
        IoMap = ioMap,
        Connection = conn ?? new ConnectionConfig(),
    };

    private static IoTag Level(IoArea area, ushort offset = 0) =>
        new(1, "level", new IoAddress(area, offset), new AnalogScale());

    private static IoTag Speed(IoArea area, ushort offset = 0) =>
        new(2, "speed", new IoAddress(area, offset), new AnalogScale());

    [Fact]
    public void WordIn_EmInputRegister_EWordOut_EmHoldingRegister_Valido()
    {
        var errors = IoMapValidator.Validate(
            Doc(new() { Level(IoArea.InputRegister), Speed(IoArea.HoldingRegister) }), Catalog());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(IoArea.DiscreteInput)]
    [InlineData(IoArea.Coil)]
    [InlineData(IoArea.HoldingRegister)] // servidor: In não escreve holding
    public void WordIn_ForaDoInputRegister_Erro(IoArea area)
    {
        var errors = IoMapValidator.Validate(
            Doc(new() { Level(area), Speed(IoArea.HoldingRegister) }), Catalog());

        Assert.Contains(errors, e => e.Code == "type-area-mismatch" && e.DeviceIds.Contains(1u));
    }

    [Theory]
    [InlineData(IoArea.DiscreteInput)]
    [InlineData(IoArea.Coil)]
    [InlineData(IoArea.InputRegister)]
    public void WordOut_ForaDoHoldingRegister_Erro(IoArea area)
    {
        var errors = IoMapValidator.Validate(
            Doc(new() { Level(IoArea.InputRegister), Speed(area) }), Catalog());

        Assert.Contains(errors, e => e.Code == "type-area-mismatch" && e.DeviceIds.Contains(2u));
    }

    [Fact]
    public void ModoCliente_WordIn_AceitaHoldingRegister()
    {
        // A Forja é master: escreve a palavra do sensor no holding do PLC.
        var errors = IoMapValidator.Validate(
            Doc(new() { Level(IoArea.HoldingRegister, 10), Speed(IoArea.HoldingRegister, 0) },
                new ConnectionConfig { Driver = ConnectionConfig.ModbusTcpClientKey }),
            Catalog());

        Assert.Empty(errors);
    }

    [Fact]
    public void EscalaDegenerada_RawMinIgualRawMax_Erro()
    {
        var doc = Doc(new()
        {
            new IoTag(1, "level", new IoAddress(IoArea.InputRegister, 0), new AnalogScale(100, 100)),
            Speed(IoArea.HoldingRegister),
        });

        var errors = IoMapValidator.Validate(doc, Catalog());

        Assert.Contains(errors, e => e.Code == "invalid-scale" && e.DeviceIds.Contains(1u));
    }

    [Fact]
    public void DoisEnderecosIguais_EmInputRegister_Erro()
    {
        // %IW0 usado por dois dispositivos → conflito, como já é para bits.
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "teste",
            Devices = new()
            {
                new DeviceInstance { Id = 1, TypeId = "sensor.level" },
                new DeviceInstance { Id = 3, TypeId = "sensor.level" },
                new DeviceInstance { Id = 2, TypeId = "actuator.vspeed" },
            },
            IoMap = new()
            {
                Level(IoArea.InputRegister),
                new IoTag(3, "level", new IoAddress(IoArea.InputRegister, 0), new AnalogScale()),
                Speed(IoArea.HoldingRegister),
            },
        };

        var errors = IoMapValidator.Validate(doc, Catalog());

        Assert.Contains(errors, e => e.Code == "duplicate-address");
    }
}
