using Forja.Anvil;
using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using Forja.Core.Editing;
using Forja.Core.Persistence;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// T047 / RF-03 / RF-08: matriz de persistência sobre o catálogo REAL do
/// repositório — cada tipo é colocado por comando de editor, endereçado,
/// salvo e recarregado; o JSON canônico tem de bater. Também mantém a lista
/// de typeIds DECLARADA e garante que todo behavior do catálogo resolve na
/// DeviceFactory (nada exige recompilar — Artigo III.2).
/// </summary>
public class CatalogRoundTripTests
{
    /// <summary>
    /// Catálogo declarado. Já foi "congelado em 17 tipos" (checkpoint da US4);
    /// o ADR 0004 abriu o congelamento e o substituiu por um critério:
    ///
    ///   tipo novo entra quando habilita uma CLASSE de lógica de CLP que o
    ///   catálogo atual não permite escrever — não por variação estética,
    ///   conveniência de cena ou completude.
    ///
    /// Esta lista continua existindo porque o valor dela nunca foi o
    /// congelamento, e sim a DELIBERAÇÃO: um tipo novo no disco quebra este
    /// teste, e quem for consertá-lo tem de passar pelo critério acima.
    /// </summary>
    private static readonly string[] CatalogTypeIds =
    {
        "actuator.pickplace",
        "actuator.piston",
        "actuator.pusher",
        "actuator.stopper",
        "chute",
        "conveyor.belt",
        "conveyor.belt.io",
        "conveyor.belt.vspeed",
        "emitter",
        "floor",
        "guide.side",
        "hmi.button",
        "hmi.light",
        "hmi.switch",
        "rack.frame",
        "sensor.height",
        "sensor.level",
        "sensor.photo",
        "sensor.proximity",
        "sink",
    };

    public static IEnumerable<object[]> TypeIds() =>
        CatalogTypeIds.Select(id => new object[] { id });

    private static DeviceCatalog LoadRealCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "catalog", "devices")))
            dir = dir.Parent;

        Assert.True(dir is not null, "diretório catalog/devices não encontrado acima do bin de teste.");
        return DeviceCatalog.LoadFromDirectory(Path.Combine(dir!.FullName, "catalog", "devices")).Require();
    }

    [Fact]
    public void CatalogoDoDiscoBateComODeclarado()
    {
        var catalog = LoadRealCatalog();
        var loaded = catalog.All.Select(t => t.TypeId).OrderBy(t => t, StringComparer.Ordinal);

        Assert.Equal(CatalogTypeIds.OrderBy(t => t, StringComparer.Ordinal), loaded);
    }

    [Fact]
    public void TodoBehaviorDoCatalogoResolveNaFactory()
    {
        var catalog = LoadRealCatalog();
        var factory = DeviceFactory.CreateDefault();

        foreach (var type in catalog.All)
        {
            var instance = new DeviceInstance { Id = 1, TypeId = type.TypeId };
            var behavior = factory.Create(instance, type); // lança se faltar registro
            Assert.NotNull(behavior);
        }
    }

    [Theory]
    [MemberData(nameof(TypeIds))]
    public void ColocarSalvarRecarregar_RoundTripCanonico(string typeId)
    {
        var catalog = LoadRealCatalog();
        var type = catalog.Get(typeId);

        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = $"round-trip {typeId}",
        };

        // Coloca em pose "difícil" (posição negativa + rotação com snap).
        doc = new PlaceDeviceCommand(typeId, new Pose(new Vec3(1.5f, 0.5f, -2.5f), 45f)).Apply(doc);
        uint id = doc.Devices[^1].Id;

        // Grava explicitamente cada default do catálogo como parâmetro.
        foreach (var def in type.ParamDefs)
        {
            if (def.Default is { } value)
                doc = new EditParamCommand(id, def.Name, value).Apply(doc);
        }

        // Endereça todas as portas (v1 digital-only: In→DI, Out→coil).
        ushort di = 0, coil = 0;
        foreach (var port in type.Ports)
        {
            var address = port.Direction == IoDirection.In
                ? new IoAddress(IoArea.DiscreteInput, di++)
                : new IoAddress(IoArea.Coil, coil++);
            doc = new ReassignAddressCommand(id, port.PortName, address).Apply(doc);
        }

        string json = SceneSerializer.Save(doc);
        var reloaded = SceneSerializer.Load(json, $"(memória:{typeId})").Require();

        Assert.Equal(json, SceneSerializer.Save(reloaded));
    }
}
