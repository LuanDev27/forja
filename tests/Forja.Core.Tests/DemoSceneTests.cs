using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Forja.Core.Persistence;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// T052 / RF-09: a cena demo do separador por altura carrega, valida e usa
/// EXATAMENTE os endereços do contrato (contracts/modbus-mapping.md) — o
/// programa OpenPLC (demo/openplc/separador.st) depende deles.
/// </summary>
public class DemoSceneTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "catalog", "devices")))
            dir = dir.Parent;
        Assert.True(dir is not null, "raiz do repositório não encontrada acima do bin de teste.");
        return dir!.FullName;
    }

    private static SceneDocument LoadSeparador() =>
        SceneSerializer.LoadFile(Path.Combine(RepoRoot(), "demo", "separador-altura.forja")).Require();

    [Fact]
    public void CarregaEValida()
    {
        string root = RepoRoot();
        var doc = LoadSeparador();
        var catalog = DeviceCatalog.LoadFromDirectory(Path.Combine(root, "catalog", "devices")).Require();

        var errors = IoMapValidator.Validate(doc, catalog);
        Assert.Empty(errors);
    }

    [Fact]
    public void RoundTripEstavel()
    {
        var doc = LoadSeparador();
        string json = SceneSerializer.Save(doc);
        var reloaded = SceneSerializer.Load(json, "(memória)").Require();

        Assert.Equal(json, SceneSerializer.Save(reloaded));
    }

    [Theory]
    [InlineData("conveyor.belt.io", "run", IoArea.Coil, (ushort)0)]
    [InlineData("sensor.height", "detect", IoArea.DiscreteInput, (ushort)0)]
    [InlineData("actuator.piston", "extend", IoArea.Coil, (ushort)1)]
    [InlineData("actuator.piston", "extended", IoArea.DiscreteInput, (ushort)1)]
    [InlineData("hmi.button", "pressed", IoArea.DiscreteInput, (ushort)2)]
    [InlineData("hmi.light", "on", IoArea.Coil, (ushort)2)]
    public void EnderecosSeguemOContrato(string typeId, string port, IoArea area, ushort offset)
    {
        var doc = LoadSeparador();
        var device = Assert.Single(doc.Devices, d => d.TypeId == typeId);
        var tag = Assert.Single(doc.IoMap, t => t.DeviceId == device.Id && t.PortName == port);

        Assert.Equal(new IoAddress(area, offset), tag.Address);
    }

    [Fact]
    public void ConexaoEhServidorModbusEmPortaNaoPrivilegiada()
    {
        var doc = LoadSeparador();

        Assert.Equal(ConnectionConfig.ModbusTcpServerKey, doc.Connection.Driver);
        Assert.True(doc.Connection.Port >= 1024, "porta privilegiada exigiria admin no Windows.");
    }
}
