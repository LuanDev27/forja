using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Forja.Core.Devices;
using Forja.Core.Loop;
using Forja.Core.Persistence;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// Spec 003 / US4 (T035–T037): a migração v1 → v2 é ADITIVA. Uma cena real da
/// Fase 1 (fixture congelada em <c>fixtures/cena-v1-fase1.forja</c>) tem de
/// abrir numa Forja v2 sem erro, com os campos analógicos no default, e
/// continuar rodando idêntica. Campo analógico malformado, ao contrário, falha
/// explícito com caminho + motivo (Artigo III.3 / VII.3).
///
/// A fixture é uma cópia CONGELADA da cena da biblioteca: ela não deve ser
/// atualizada quando as cenas de `plc/` evoluírem — o valor dela é justamente
/// ser um arquivo v1 tal como a Fase 1 o escrevia.
/// </summary>
public class SchemaMigrationV1V2Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "catalog", "devices")))
            dir = dir.Parent;
        Assert.True(dir is not null, "raiz do repositório não encontrada acima do bin de teste.");
        return dir!.FullName;
    }

    private static string FixturePath() => Path.Combine(
        RepoRoot(), "tests", "Forja.Core.Tests", "fixtures", "cena-v1-fase1.forja");

    private static string FixtureJson() => File.ReadAllText(FixturePath());

    private static DeviceCatalog Catalog() =>
        DeviceCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "catalog", "devices")).Require();

    // ---- T035: carga aditiva (AS1) ----

    [Fact]
    public void CenaV1_CarregaSemErroECarimbaV2()
    {
        var doc = SceneSerializer.Load(FixtureJson(), FixturePath()).Require();

        Assert.Equal(SceneDocument.CurrentSchemaVersion, doc.SchemaVersion);
        Assert.Equal(2, doc.SchemaVersion);
        Assert.Equal(7, doc.Devices.Count);
        Assert.Equal(4, doc.IoMap.Count);
        Assert.Equal(42UL, doc.Seed);
    }

    [Fact]
    public void CenaV1_TodoPontoVemComEscalaNula()
    {
        var doc = SceneSerializer.Load(FixtureJson(), FixturePath()).Require();

        // Aditivo: nada em v1 falava de escala, então todo ponto é de bit.
        Assert.All(doc.IoMap, tag => Assert.Null(tag.Scale));
    }

    [Fact]
    public void PortasDosTiposDaCenaV1_SaoBoolPorDefault()
    {
        var doc = SceneSerializer.Load(FixtureJson(), FixturePath()).Require();
        var catalog = Catalog();

        foreach (var tag in doc.IoMap)
        {
            var device = Assert.Single(doc.Devices, d => d.Id == tag.DeviceId);
            var port = Assert.Single(catalog.Get(device.TypeId).Ports, p => p.PortName == tag.PortName);
            Assert.Equal(PortType.Bool, port.Type);
        }
    }

    [Fact]
    public void CenaV1_ContinuaValidaNaForjaV2()
    {
        var doc = SceneSerializer.Load(FixtureJson(), FixturePath()).Require();

        var errors = IoMapValidator.Validate(doc, Catalog());

        Assert.Empty(errors);
    }

    [Fact]
    public void CenaV1_RodaIdenticaAUmaCenaDeclaradaV2()
    {
        // A migração não pode mudar NADA observável: a mesma cena declarada em
        // v1 e em v2 tem de produzir o mesmo hash de estado tick a tick.
        var deV1 = SceneSerializer.Load(FixtureJson(), FixturePath()).Require();
        var deV2 = SceneSerializer.Load(
            FixtureJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            "(v2 em memória)").Require();

        Assert.Equal(HashApos(deV1, 120), HashApos(deV2, 120));
    }

    private static ulong HashApos(SceneDocument doc, int ticks)
    {
        using var loop = new SimulationLoop(
            doc, Catalog(), DeviceFactory.CreateDefault(), new FakePhysicsWorld(), _ => new FakeDriver());

        loop.Enqueue(new SetModeCommand(SimMode.Run));
        for (int i = 0; i < ticks; i++)
            loop.Tick();

        Assert.Equal(SimMode.Run, loop.Mode);
        return loop.ComputeStateHash();
    }

    // ---- T036: round-trip (AS2) ----

    [Fact]
    public void CenaV1_SalvaComVersaoNovaEDefaultsExplicitos()
    {
        var doc = SceneSerializer.Load(FixtureJson(), FixturePath()).Require();

        string json = SceneSerializer.Save(doc);

        Assert.Contains("\"schemaVersion\": 2", json);
        // Campo aditivo aparece explícito (nulo), não sumido: quem for ler o
        // arquivo depois vê que a escala existe e está vazia.
        Assert.Contains("\"scale\": null", json);
    }

    [Fact]
    public void CenaV1_RoundTripEstavelEmV2()
    {
        var doc = SceneSerializer.Load(FixtureJson(), FixturePath()).Require();

        string json = SceneSerializer.Save(doc);
        var recarregada = SceneSerializer.Load(json, "(memória)").Require();

        Assert.Equal(json, SceneSerializer.Save(recarregada));
        Assert.Equal(
            doc.Devices.Select(d => d.Id),
            recarregada.Devices.Select(d => d.Id));
        Assert.All(recarregada.IoMap, tag => Assert.Null(tag.Scale));
        Assert.Empty(IoMapValidator.Validate(recarregada, Catalog()));
    }

    // ---- T037: campo analógico malformado falha explícito (AS3) ----

    [Fact]
    public void CampoDesconhecidoDentroDaEscala_FalhaComCaminhoEMotivo()
    {
        string json = """
            {
              "schemaVersion": 2,
              "name": "escala com campo inventado",
              "devices": [ { "id": 1, "typeId": "sensor.level" } ],
              "ioMap": [
                {
                  "deviceId": 1,
                  "portName": "level",
                  "address": { "area": "InputRegister", "offset": 0 },
                  "scale": { "rawMin": 0, "rawMax": 65535, "unidade": "cm" }
                }
              ]
            }
            """;

        var result = SceneSerializer.Load(json, "escala-torta.forja");

        Assert.False(result.Ok);
        Assert.Contains("escala-torta.forja", result.Error);
        Assert.Contains("unidade", result.Error);
    }

    [Fact]
    public void EscalaComTipoErrado_FalhaComCaminhoEMotivo()
    {
        string json = """
            {
              "schemaVersion": 2,
              "name": "escala com tipo errado",
              "devices": [ { "id": 1, "typeId": "sensor.level" } ],
              "ioMap": [
                {
                  "deviceId": 1,
                  "portName": "level",
                  "address": { "area": "InputRegister", "offset": 0 },
                  "scale": { "rawMin": "zero", "rawMax": 65535 }
                }
              ]
            }
            """;

        var result = SceneSerializer.Load(json, "escala-tipo.forja");

        Assert.False(result.Ok);
        Assert.Contains("escala-tipo.forja", result.Error);
        Assert.Contains("JSON path:", result.Error);
    }

    [Fact]
    public void EscalaForaDaFaixaDeUshort_FalhaEmVezDeDarAVolta()
    {
        // 70000 não cabe num ushort: tem de estourar na carga, não virar 4464.
        string json = """
            {
              "schemaVersion": 2,
              "name": "escala fora da faixa",
              "devices": [ { "id": 1, "typeId": "sensor.level" } ],
              "ioMap": [
                {
                  "deviceId": 1,
                  "portName": "level",
                  "address": { "area": "InputRegister", "offset": 0 },
                  "scale": { "rawMin": 0, "rawMax": 70000 }
                }
              ]
            }
            """;

        var result = SceneSerializer.Load(json, "escala-estourada.forja");

        Assert.False(result.Ok);
        Assert.Contains("escala-estourada.forja", result.Error);
    }
}
