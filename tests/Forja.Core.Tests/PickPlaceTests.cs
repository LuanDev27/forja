using Forja.Anvil;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using Forja.Core.Io;
using Forja.Core.Physics;
using Forja.Core.State;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// Spec 002 / US1: a garra do pick-and-place. Testes de LÓGICA com física
/// falsa (Artigo V.1) — o comportamento físico de verdade é provado pelos
/// cenários headless.
/// </summary>
public class PickPlaceTests
{
    private const uint DeviceId = 1;

    private sealed class Rig
    {
        public required FakePhysicsWorld Physics { get; init; }
        public required PartsManager Parts { get; init; }
        public required IoTable Io { get; init; }
        public required PickPlace Device { get; init; }
        public required SimContext Ctx { get; init; }

        public void Tick(int times = 1)
        {
            for (int i = 0; i < times; i++)
                Device.Tick(Ctx);
        }

        /// <summary>Força a coil, que é exatamente o que o modo manual faz.</summary>
        public void Set(string port, bool value)
        {
            ushort offset = port switch
            {
                "advance" => 0, "lower" => 1, "grip" => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(port), port, "saída desconhecida"),
            };
            Io.Force(new IoAddress(IoArea.Coil, offset), value);
        }

        /// <summary>Lê um bit de entrada pela view (não há getter direto).</summary>
        public bool In(string port)
        {
            ushort offset = port switch
            {
                "advanced" => 0, "retracted" => 1, "lowered" => 2,
                "raised" => 3, "holding" => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(port), port, "entrada desconhecida"),
            };
            foreach (var row in Io.BuildView())
            {
                if (row.Address.Area == IoArea.DiscreteInput && row.Address.Offset == offset)
                    return row.Value;
            }
            return false;
        }
    }

    private static DeviceTypeDef TypeDef() => new()
    {
        TypeId = "actuator.pickplace",
        Category = DeviceCategory.Actuator,
        DisplayName = "Pick-and-place",
        Behavior = "pick-place",
        Ports = new()
        {
            new PortDef("advance", IoDirection.Out),
            new PortDef("lower", IoDirection.Out),
            new PortDef("grip", IoDirection.Out),
            new PortDef("advanced", IoDirection.In),
            new PortDef("retracted", IoDirection.In),
            new PortDef("lowered", IoDirection.In),
            new PortDef("raised", IoDirection.In),
            new PortDef("holding", IoDirection.In),
        },
    };

    private static Rig Build()
    {
        var catalog = DeviceCatalog.FromDefs(new[] { TypeDef() }).Require();

        var instance = new DeviceInstance
        {
            Id = DeviceId,
            TypeId = "actuator.pickplace",
            Transform = new Pose(new Vec3(0, 1f, 0), 0),
        };

        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "pickplace",
            Devices = new() { instance },
            IoMap = new()
            {
                new IoTag(DeviceId, "advance", new IoAddress(IoArea.Coil, 0)),
                new IoTag(DeviceId, "lower", new IoAddress(IoArea.Coil, 1)),
                new IoTag(DeviceId, "grip", new IoAddress(IoArea.Coil, 2)),
                new IoTag(DeviceId, "advanced", new IoAddress(IoArea.DiscreteInput, 0)),
                new IoTag(DeviceId, "retracted", new IoAddress(IoArea.DiscreteInput, 1)),
                new IoTag(DeviceId, "lowered", new IoAddress(IoArea.DiscreteInput, 2)),
                new IoTag(DeviceId, "raised", new IoAddress(IoArea.DiscreteInput, 3)),
                new IoTag(DeviceId, "holding", new IoAddress(IoArea.DiscreteInput, 4)),
            },
        };

        var physics = new FakePhysicsWorld();
        var parts = new PartsManager(physics);
        var io = new IoTable(doc, catalog);

        var factory = new DeviceFactory();
        factory.Register("pick-place", () => new PickPlace());
        var device = (PickPlace)factory.Create(instance, TypeDef());

        var ctx = new SimContext
        {
            Io = io,
            Physics = physics,
            Parts = parts,
            Random = new SeededRandom(1),
        };

        device.Build(ctx);
        return new Rig
        {
            Physics = physics, Parts = parts, Io = io, Device = device, Ctx = ctx,
        };
    }

    private static Part Spawn(Rig rig) =>
        rig.Parts.SpawnBox(new PartKind("S", "plastic"), new Pose(new Vec3(0, 1f, 0), 0));

    [Fact]
    public void GarraNoVazio_NaoPrendeENaoTrava()
    {
        var rig = Build();
        rig.Set("grip", true);
        rig.Tick(5);

        Assert.Equal(0u, rig.Device.HeldPartId);
    }

    [Fact]
    public void Agarra_PecaAoAlcance()
    {
        var rig = Build();
        var part = Spawn(rig);
        rig.Physics.QueryResult.Add(part.Id);

        rig.Set("grip", true);
        rig.Tick();

        Assert.Equal(part.Id, rig.Device.HeldPartId);
        Assert.Equal(BodyKind.Kinematic, ((FakePhysicsWorld.FakeBody)part.Body).Kind);
    }

    /// <summary>
    /// R3: QueryBox não garante ordem. A física falsa devolve a lista
    /// INVERTIDA de propósito — se o comportamento pegasse "a primeira da
    /// lista" em vez de ordenar por id, este teste falharia, que é
    /// exatamente o que o Artigo I.3 exige que seja detectável.
    /// </summary>
    [Fact]
    public void ComVariasPecasAoAlcance_AgarraADeMenorId()
    {
        var rig = Build();
        var a = Spawn(rig);
        var b = Spawn(rig);
        var c = Spawn(rig);
        rig.Physics.QueryResult.AddRange(new[] { a.Id, b.Id, c.Id });

        rig.Set("grip", true);
        rig.Tick();

        uint menor = Math.Min(a.Id, Math.Min(b.Id, c.Id));
        Assert.Equal(menor, rig.Device.HeldPartId);
    }

    [Fact]
    public void Soltar_DevolveARigidoEAcorda()
    {
        var rig = Build();
        var part = Spawn(rig);
        rig.Physics.QueryResult.Add(part.Id);

        rig.Set("grip", true);
        rig.Tick();
        rig.Set("grip", false);
        rig.Tick();

        var body = (FakePhysicsWorld.FakeBody)part.Body;
        Assert.Equal(0u, rig.Device.HeldPartId);
        Assert.Equal(BodyKind.Rigid, body.Kind);
        Assert.True(body.WakeCalls > 0, "peça solta precisa ser acordada (R5).");
    }

    [Fact]
    public void Teardown_DesfazVinculo()
    {
        var rig = Build();
        var part = Spawn(rig);
        rig.Physics.QueryResult.Add(part.Id);

        rig.Set("grip", true);
        rig.Tick();
        rig.Device.Teardown(rig.Ctx);

        Assert.Equal(0u, rig.Device.HeldPartId);
        Assert.Equal(BodyKind.Rigid, ((FakePhysicsWorld.FakeBody)part.Body).Kind);
    }

    [Fact]
    public void PecaRemovidaDuranteOTransporte_NaoDeixaIdOrfao()
    {
        var rig = Build();
        var part = Spawn(rig);
        rig.Physics.QueryResult.Add(part.Id);

        rig.Set("grip", true);
        rig.Tick();
        rig.Parts.Remove(part.Id);
        rig.Tick();

        Assert.Equal(0u, rig.Device.HeldPartId);
    }

    /// <summary>
    /// Contrato: no meio do curso os DOIS fins de curso do eixo são falsos.
    /// Se `retracted` fosse `NOT advanced`, ele ficaria verdadeiro durante todo
    /// o avanço e a sequência do CLP pularia de passo antes da hora.
    /// </summary>
    [Fact]
    public void FinsDeCurso_NoMeioDoCursoAmbosSaoFalsos()
    {
        var rig = Build();
        rig.Set("advance", true);
        rig.Tick(); // um tick só: extX ainda muito menor que strokeX

        Assert.False(rig.In("advanced"));
        Assert.False(rig.In("retracted"));
    }

    [Fact]
    public void FinsDeCurso_NosExtremosExatamenteUmEVerdadeiro()
    {
        var rig = Build();

        // Repouso: recolhido e no alto.
        rig.Tick();
        Assert.True(rig.In("retracted"));
        Assert.False(rig.In("advanced"));
        Assert.True(rig.In("raised"));
        Assert.False(rig.In("lowered"));

        // Avança até o fim (0,8 m a 0,8 m/s = 1 s = 60 ticks, com folga).
        rig.Set("advance", true);
        rig.Set("lower", true);
        rig.Tick(120);

        Assert.True(rig.In("advanced"));
        Assert.False(rig.In("retracted"));
        Assert.True(rig.In("lowered"));
        Assert.False(rig.In("raised"));
    }

    [Fact]
    public void Holding_RefleteAPecaPresa()
    {
        var rig = Build();
        var part = Spawn(rig);
        rig.Physics.QueryResult.Add(part.Id);

        rig.Tick();
        Assert.False(rig.In("holding"));

        rig.Set("grip", true);
        rig.Tick();
        Assert.True(rig.In("holding"));
    }

    [Fact]
    public void Hash_MudaQuandoAPecaPresaMuda()
    {
        static ulong HashOf(Rig rig)
        {
            var h = StateHasher.Create();
            rig.Device.WriteState(ref h);
            return h.Hash;
        }

        var vazio = Build();
        vazio.Tick();
        ulong semPeca = HashOf(vazio);

        var comPeca = Build();
        var part = Spawn(comPeca);
        comPeca.Physics.QueryResult.Add(part.Id);
        comPeca.Set("grip", true);
        comPeca.Tick();

        Assert.NotEqual(semPeca, HashOf(comPeca));
    }
}
