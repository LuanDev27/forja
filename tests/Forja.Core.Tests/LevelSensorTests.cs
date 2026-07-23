using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using Forja.Core.Io;
using Forja.Core.Physics;
using Forja.Core.State;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// US1 (spec 003): o sensor de nível analógico publica um valor que o CLP lê
/// em `%IW`. Física falsa (Artigo V.1): o raycast devolve uma superfície numa
/// altura conhecida e o sensor reporta o % de enchimento.
/// </summary>
public class LevelSensorTests
{
    private const uint DeviceId = 1;

    private static DeviceTypeDef TypeDef() => new()
    {
        TypeId = "sensor.level",
        Category = DeviceCategory.Sensor,
        DisplayName = "Sensor de nível",
        Behavior = "level-sensor",
        Ports = new() { new PortDef("level", IoDirection.In, PortType.Word) },
        ParamDefs = new()
        {
            new ParamDef { Name = "range", Type = "float", Default = Num(1.0) },
            new ParamDef { Name = "euMin", Type = "float", Default = Num(0) },
            new ParamDef { Name = "euMax", Type = "float", Default = Num(100) },
        },
    };

    private static JsonElement Num(double v) => JsonSerializer.SerializeToElement(v);

    private sealed class Rig
    {
        public required FakePhysicsWorld Physics { get; init; }
        public required PartsManager Parts { get; init; }
        public required IoTable Io { get; init; }
        public required LevelSensor Device { get; init; }
        public required SimContext Ctx { get; init; }

        public ushort Raw()
        {
            Device.Tick(Ctx);
            return Io.BuildInputSnapshot(1).Words.Span[0];
        }
    }

    private static Rig Build(AnalogScale? scale = null)
    {
        var catalog = DeviceCatalog.FromDefs(new[] { TypeDef() }).Require();
        var instance = new DeviceInstance
        {
            Id = DeviceId,
            TypeId = "sensor.level",
            Transform = new Pose(new Vec3(0, 1f, 0), 0), // topo do tanque em Y=1, olha −Y
        };
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "nível",
            Devices = new() { instance },
            IoMap = new()
            {
                new IoTag(DeviceId, "level", new IoAddress(IoArea.InputRegister, 0), scale ?? new AnalogScale()),
            },
        };

        var physics = new FakePhysicsWorld();
        var parts = new PartsManager(physics);
        var io = new IoTable(doc, catalog);
        var device = (LevelSensor)new DeviceFactory()
            .Also(f => f.Register("level-sensor", () => new LevelSensor()))
            .Create(instance, TypeDef());

        var ctx = new SimContext
        {
            Io = io, Physics = physics, Parts = parts, Random = new SeededRandom(1),
        };
        device.Build(ctx);
        return new Rig { Physics = physics, Parts = parts, Io = io, Device = device, Ctx = ctx };
    }

    /// <summary>Coloca a superfície do material na altura Y e devolve o rig pronto.</summary>
    private static Rig WithSurfaceAt(float surfaceY, AnalogScale? scale = null)
    {
        var rig = Build(scale);
        var part = rig.Parts.SpawnBox(new PartKind("S", "plastic"), new Pose(new Vec3(0, surfaceY, 0), 0));
        rig.Physics.RayResult = new RayHit(part.Id, new Vec3(0, surfaceY, 0));
        return rig;
    }

    [Fact]
    public void TanqueVazio_SemEco_NivelZero()
    {
        var rig = Build();
        rig.Physics.RayResult = null; // feixe livre — nada abaixo

        Assert.Equal(0, rig.Raw());
    }

    [Theory]
    [InlineData(0.0f, 0)]      // superfície no fundo do alcance → 0%
    [InlineData(0.5f, 32768)] // meio tanque → 50% → ~meio da escala bruta
    [InlineData(1.0f, 65535)] // superfície no sensor → 100% → topo
    [InlineData(0.25f, 16384)]
    public void Superficie_MapeiaParaBruto(float surfaceY, int rawEsperado)
    {
        var rig = WithSurfaceAt(surfaceY);

        Assert.Equal((ushort)rawEsperado, rig.Raw());
    }

    [Fact]
    public void SuperficieAcimaDoSensor_Satura_NaoEstoura()
    {
        var rig = WithSurfaceAt(1.5f); // acima do sensor → fill saturado em 100%

        Assert.Equal(65535, rig.Raw());
    }

    [Fact]
    public void EcoQueNaoEPeca_NaoContaComoNivel()
    {
        var rig = Build();
        rig.Physics.RayResult = new RayHit(999u, new Vec3(0, 0.8f, 0)); // id que não é peça

        Assert.Equal(0, rig.Raw());
    }

    [Fact]
    public void DoisCartoesDiferentes_MesmoNivel_BrutosDiferentes()
    {
        var cheio = WithSurfaceAt(0.5f, new AnalogScale(0, 65535));
        var metade = WithSurfaceAt(0.5f, new AnalogScale(0, 32767));

        Assert.NotEqual(cheio.Raw(), metade.Raw());
    }

    [Fact]
    public void EstadoInternoEntraNoHash()
    {
        var a = WithSurfaceAt(0.4f);
        var b = WithSurfaceAt(0.6f);
        a.Device.Tick(a.Ctx);
        b.Device.Tick(b.Ctx);

        var ha = StateHasher.Create();
        a.Device.WriteState(ref ha);
        var hb = StateHasher.Create();
        b.Device.WriteState(ref hb);

        Assert.NotEqual(ha.Hash, hb.Hash);
    }
}

internal static class FactoryExt
{
    /// <summary>Açúcar para configurar a fábrica inline no teste.</summary>
    public static DeviceFactory Also(this DeviceFactory f, Action<DeviceFactory> cfg)
    {
        cfg(f);
        return f;
    }
}
