using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Xunit;

namespace Forja.Anvil.Tests;

/// <summary>
/// T027 / RF-05 / Artigo VI.3: endereço duplicado é ERRO que bloqueia Run e a
/// mensagem cita OS DOIS dispositivos envolvidos.
/// </summary>
public class DuplicateAddressTests
{
    [Fact]
    public void MesmoEndereco_DoisDevices_ErroCitaAmbos()
    {
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "duplicado",
            Devices = new() { TestCatalog.Sensor(1), TestCatalog.Sensor(2) },
            IoMap = new() { TestCatalog.Di(1, 5), TestCatalog.Di(2, 5) },
        };

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        var dup = Assert.Single(errors, e => e.Code == "duplicate-address");
        Assert.Contains(1u, dup.DeviceIds);
        Assert.Contains(2u, dup.DeviceIds);
        Assert.Contains("1", dup.Message);
        Assert.Contains("2", dup.Message);
        Assert.Contains("%IX0.5", dup.Message); // notação dupla da UI (Q2)
    }

    [Fact]
    public void MesmoOffset_AreasDiferentes_NaoEDuplicado()
    {
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "areas-distintas",
            Devices = new() { TestCatalog.Sensor(1), TestCatalog.Actuator(2) },
            IoMap = new() { TestCatalog.Di(1, 0), TestCatalog.Coil(2, 0) },
        };

        var errors = IoMapValidator.Validate(doc, TestCatalog.Build());

        Assert.DoesNotContain(errors, e => e.Code == "duplicate-address");
    }
}
