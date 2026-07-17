using Forja.Anvil.Scene;
using Forja.Core.Persistence;
using Xunit;

namespace Forja.Core.Tests;

public class SceneSerializerTests
{
    private static SceneDocument Sample() => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "demo",
        Seed = 42,
        Devices = new()
        {
            new DeviceInstance { Id = 3, TypeId = "actuator.test" },
            new DeviceInstance { Id = 1, TypeId = "sensor.test" },
        },
        IoMap = new()
        {
            new IoTag(3, "extend", new IoAddress(IoArea.Coil, 1)),
            new IoTag(1, "detect", new IoAddress(IoArea.DiscreteInput, 0)),
        },
        Connection = new ConnectionConfig { Driver = ConnectionConfig.ModbusTcpServerKey, Port = 1502 },
    };

    [Fact]
    public void RoundTrip_PreservaConteudoEmFormaCanonica()
    {
        var doc = Sample();

        string json = SceneSerializer.Save(doc);
        var loaded = SceneSerializer.Load(json, "mem.forja").Require();

        // Igualdade estrutural via forma canônica (S2: devices por id crescente).
        Assert.Equal(json, SceneSerializer.Save(loaded));
        Assert.Equal(new uint[] { 1, 3 }, loaded.Devices.Select(d => d.Id));
        Assert.Equal(42UL, loaded.Seed);
        Assert.Equal(1502, loaded.Connection.Port);
    }

    [Fact]
    public void SchemaVersionAusente_FalhaComCaminho()
    {
        var result = SceneSerializer.Load("""{ "name": "x" }""", "quebrado.forja");

        Assert.False(result.Ok);
        Assert.Contains("quebrado.forja", result.Error);
        Assert.Contains("schemaVersion", result.Error);
    }

    [Fact]
    public void VersaoFutura_FalhaExplicando()
    {
        var result = SceneSerializer.Load(
            """{ "schemaVersion": 999, "name": "x" }""", "futuro.forja");

        Assert.False(result.Ok);
        Assert.Contains("999", result.Error);
    }

    [Fact]
    public void VersaoAntigaSemMigracao_Falha()
    {
        var result = SceneSerializer.Load(
            """{ "schemaVersion": 0, "name": "x" }""", "antigo.forja");

        Assert.False(result.Ok);
        Assert.Contains("migração", result.Error);
    }

    [Fact]
    public void CampoDesconhecido_Rejeitado()
    {
        var result = SceneSerializer.Load(
            """{ "schemaVersion": 1, "name": "x", "naoExiste": true }""", "extra.forja");

        Assert.False(result.Ok);
        Assert.Contains("extra.forja", result.Error);
    }

    [Fact]
    public void IdDuplicado_Rejeitado()
    {
        var json = """
            {
              "schemaVersion": 1,
              "name": "x",
              "devices": [
                { "id": 7, "typeId": "a" },
                { "id": 7, "typeId": "b" }
              ]
            }
            """;

        var result = SceneSerializer.Load(json, "dup.forja");

        Assert.False(result.Ok);
        Assert.Contains("7", result.Error);
    }

    [Fact]
    public void JsonInvalido_FalhaComMotivo()
    {
        var result = SceneSerializer.Load("{ isso não é json", "lixo.forja");

        Assert.False(result.Ok);
        Assert.Contains("lixo.forja", result.Error);
    }
}
