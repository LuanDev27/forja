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

/// <summary>
/// Amortecimento do sensor de nível (23/07): o ruído deste sensor é de IMPULSO
/// — o feixe cai numa fresta entre peças, atravessa até o fundo e a leitura
/// despenca a 0 por alguns ticks. Medido na bancada, isso derrubava a malha do
/// cenário 07: ~30 trocas de velocidade em 60 s, cada estado durando 0,1–0,5 s.
/// A banda morta do programa NÃO filtra isso (ela filtra tremor de quantização
/// em torno do setpoint — problema diferente). O conserto mora no instrumento.
/// </summary>
public class LevelSensorDampingTests
{
    private static JsonElement Num(double v) => JsonSerializer.SerializeToElement(v);

    private static DeviceTypeDef TypeDef(int damping) => new()
    {
        TypeId = "sensor.level",
        Category = DeviceCategory.Sensor,
        DisplayName = "Sensor de nível",
        Behavior = "level-sensor",
        Ports = new() { new PortDef("level", IoDirection.In, PortType.Word) },
        ParamDefs = new()
        {
            new ParamDef { Name = "range", Type = "float", Default = Num(1.0) },
            new ParamDef { Name = "damping", Type = "int", Default = Num(damping) },
            new ParamDef { Name = "euMin", Type = "float", Default = Num(0) },
            new ParamDef { Name = "euMax", Type = "float", Default = Num(100) },
        },
    };

    private sealed class Rig
    {
        public required FakePhysicsWorld Physics { get; init; }
        public required PartsManager Parts { get; init; }
        public required LevelSensor Device { get; init; }
        public required SimContext Ctx { get; init; }
        public required uint PartId { get; init; }

        /// <summary>Alimenta o sensor com uma superfície em <paramref name="pct"/>%
        /// por N ticks (sensor em Y=1, alcance 1 → altura = pct/100).</summary>
        public void Alimentar(float pct, int ticks = 1)
        {
            Physics.RayResult = new RayHit(PartId, new Vec3(0, pct / 100f, 0));
            for (int i = 0; i < ticks; i++)
                Device.Tick(Ctx);
        }

        /// <summary>Simula a fresta: o feixe não acha peça nenhuma.</summary>
        public void Fresta(int ticks = 1)
        {
            Physics.RayResult = null;
            for (int i = 0; i < ticks; i++)
                Device.Tick(Ctx);
        }
    }

    private static Rig Build(int damping)
    {
        var def = TypeDef(damping);
        var instance = new DeviceInstance
        {
            Id = 1,
            TypeId = "sensor.level",
            Transform = new Pose(new Vec3(0, 1f, 0), 0),
            Params = new()
            {
                ["range"] = Num(1.0),
                ["damping"] = Num(damping),
            },
        };
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "nível",
            Devices = new() { instance },
            IoMap = new() { new IoTag(1, "level", new IoAddress(IoArea.InputRegister, 0), new AnalogScale()) },
        };

        var physics = new FakePhysicsWorld();
        var parts = new PartsManager(physics);
        var device = (LevelSensor)DeviceFactory.CreateDefault().Create(instance, def);
        var ctx = new SimContext
        {
            Io = new IoTable(doc, DeviceCatalog.FromDefs(new[] { def }).Require()),
            Physics = physics,
            Parts = parts,
            Random = new SeededRandom(1),
        };
        device.Build(ctx);
        var part = parts.SpawnBox(new PartKind("S", "plastic"), new Pose(Vec3.Zero, 0));
        return new Rig { Physics = physics, Parts = parts, Device = device, Ctx = ctx, PartId = part.Id };
    }

    [Fact]
    public void SemAmortecimento_ORuidoPassaDireto()
    {
        // Controle: com damping=1 (default) o sensor segue como na Fase 2.
        var rig = Build(damping: 1);
        rig.Alimentar(70, ticks: 10);
        Assert.Equal(70f, rig.Device.Level, 1);

        rig.Fresta();

        Assert.Equal(0f, rig.Device.Level, 1); // a queda espúria chega ao CLP
    }

    [Fact]
    public void ComAmortecimento_FrestaIsoladaNaoChegaAoClp()
    {
        var rig = Build(damping: 9);
        rig.Alimentar(70, ticks: 9);
        Assert.Equal(70f, rig.Device.Level, 1);

        rig.Fresta(ticks: 4); // minoria da janela

        Assert.Equal(70f, rig.Device.Level, 1); // mediana descarta
        Assert.Equal(0f, rig.Device.LevelCru, 1); // mas o cru registra o que o feixe viu
    }

    [Fact]
    public void MedianaNaoEhMedia_EhIssoQueRejeitaOImpulso()
    {
        // Guarda contra alguém "simplificar" a mediana para média: com 4 frestas
        // em 9 ticks, a média cairia para ~39% e a malha chavearia. A mediana
        // não se move. Este teste é o que trava essa troca.
        var rig = Build(damping: 9);
        rig.Alimentar(70, ticks: 5);
        rig.Fresta(ticks: 4);

        float media = (70f * 5 + 0f * 4) / 9f;
        Assert.True(media < 40f, "premissa do teste: a média cairia bastante");
        Assert.Equal(70f, rig.Device.Level, 1);
    }

    [Fact]
    public void MudancaSustentada_Passa_SoQueComAtraso()
    {
        // Amortecer não pode virar cegueira: mudança de verdade tem de chegar.
        var rig = Build(damping: 9);
        rig.Alimentar(20, ticks: 9);
        Assert.Equal(20f, rig.Device.Level, 1);

        rig.Alimentar(80, ticks: 4);
        Assert.Equal(20f, rig.Device.Level, 1);  // ainda minoria da janela

        rig.Alimentar(80, ticks: 5);
        Assert.Equal(80f, rig.Device.Level, 1);  // virou maioria: passou
    }

    [Fact]
    public void JanelaEntraNoHash()
    {
        // A janela decide as saídas dos próximos ticks: dois sensores com o
        // mesmo nível publicado mas históricos diferentes NÃO são o mesmo
        // estado (Artigo I.4).
        var a = Build(damping: 5);
        a.Alimentar(50, ticks: 5);

        var b = Build(damping: 5);
        b.Alimentar(50, ticks: 3);
        b.Alimentar(90, ticks: 1);
        b.Alimentar(50, ticks: 1);

        Assert.Equal(a.Device.Level, b.Device.Level, 1); // mesma mediana

        var ha = StateHasher.Create();
        a.Device.WriteState(ref ha);
        var hb = StateHasher.Create();
        b.Device.WriteState(ref hb);

        Assert.NotEqual(ha.Hash, hb.Hash);
    }
}
