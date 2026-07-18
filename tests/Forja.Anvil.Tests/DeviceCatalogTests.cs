using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Xunit;

namespace Forja.Anvil.Tests;

public class DeviceCatalogTests
{
    private static DeviceTypeDef Def(string typeId) => new()
    {
        TypeId = typeId,
        Category = DeviceCategory.Passive,
        DisplayName = typeId,
        Behavior = "static-body",
    };

    [Fact]
    public void TypeIdDuplicado_FalhaExplicita()
    {
        var result = DeviceCatalog.FromDefs(new[] { Def("floor"), Def("floor") });

        Assert.False(result.Ok);
        Assert.Contains("floor", result.Error);
    }

    [Fact]
    public void BehaviorVazio_Falha()
    {
        var def = new DeviceTypeDef
        {
            TypeId = "x",
            Category = DeviceCategory.Passive,
            DisplayName = "x",
            Behavior = "",
        };

        var result = DeviceCatalog.FromDefs(new[] { def });

        Assert.False(result.Ok);
        Assert.Contains("behavior", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortaDuplicadaNoTipo_Falha()
    {
        var def = new DeviceTypeDef
        {
            TypeId = "x",
            Category = DeviceCategory.Sensor,
            DisplayName = "x",
            Behavior = "photo-sensor",
            Ports = new()
            {
                new PortDef("detect", IoDirection.In),
                new PortDef("detect", IoDirection.In),
            },
        };

        var result = DeviceCatalog.FromDefs(new[] { def });

        Assert.False(result.Ok);
        Assert.Contains("detect", result.Error);
    }

    [Fact]
    public void DiretorioInexistente_FalhaComCaminho()
    {
        var result = DeviceCatalog.LoadFromDirectory(@"Z:\nao\existe\catalogo");

        Assert.False(result.Ok);
        Assert.Contains("catalogo", result.Error);
    }

    [Fact]
    public void FlushToGround_CarregaDoJsonEDefaultFalse()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forja-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "piso.json"), """
                {
                  "typeId": "floor",
                  "category": "Passive",
                  "displayName": "Piso",
                  "behavior": "static-body",
                  "flushToGround": true
                }
                """);
            File.WriteAllText(Path.Combine(dir, "rack.json"), """
                {
                  "typeId": "rack.frame",
                  "category": "Passive",
                  "displayName": "Grade",
                  "behavior": "static-body"
                }
                """);

            var result = DeviceCatalog.LoadFromDirectory(dir);
            Assert.True(result.Ok, result.Error);
            Assert.True(result.Value!.Get("floor").FlushToGround);
            Assert.False(result.Value!.Get("rack.frame").FlushToGround);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_CarregaJsonEDetectaDuplicado()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forja-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string json = """
                {
                  "typeId": "conveyor.belt",
                  "category": "Transport",
                  "displayName": "Esteira",
                  "behavior": "conveyor",
                  "ports": []
                }
                """;
            File.WriteAllText(Path.Combine(dir, "a.json"), json);

            var ok = DeviceCatalog.LoadFromDirectory(dir);
            Assert.True(ok.Ok, ok.Error);
            Assert.Equal("Esteira", ok.Value!.Get("conveyor.belt").DisplayName);

            File.WriteAllText(Path.Combine(dir, "b.json"), json);
            var dup = DeviceCatalog.LoadFromDirectory(dir);
            Assert.False(dup.Ok);
            Assert.Contains("conveyor.belt", dup.Error);
            Assert.Contains("b.json", dup.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
