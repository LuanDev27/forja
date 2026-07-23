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
/// US2 (spec 003): a esteira de velocidade variável obedece a um setpoint que o
/// CLP escreve em `%QW`. Força-se o holding register (o que o modo manual /
/// master faz) e confere-se a velocidade de superfície aplicada.
/// </summary>
public class VariableSpeedConveyorTests
{
    private const uint DeviceId = 1;
    private static readonly IoAddress SpeedAddr = new(IoArea.HoldingRegister, 0);

    private static DeviceTypeDef TypeDef() => new()
    {
        TypeId = "conveyor.belt.vspeed",
        Category = DeviceCategory.Transport,
        DisplayName = "Esteira VV",
        Behavior = "conveyor-vspeed",
        Ports = new() { new PortDef("speed", IoDirection.Out, PortType.Word) },
        ParamDefs = new()
        {
            new ParamDef { Name = "euMin", Type = "float", Default = Num(0) },
            new ParamDef { Name = "euMax", Type = "float", Default = Num(2) },
        },
    };

    private static JsonElement Num(double v) => JsonSerializer.SerializeToElement(v);

    private sealed class Rig
    {
        public required FakePhysicsWorld Physics { get; init; }
        public required IoTable Io { get; init; }
        public required VariableSpeedConveyor Device { get; init; }
        public required SimContext Ctx { get; init; }

        /// <summary>Master escreve o setpoint bruto no %QW e a esteira dá um passo.</summary>
        public float SpeedFor(ushort raw)
        {
            Io.ForceWord(SpeedAddr, raw);
            Device.Tick(Ctx);
            return Physics.Bodies[DeviceId].SurfaceVelocity.X; // rotY=0 → eixo +X
        }
    }

    private static Rig Build()
    {
        var catalog = DeviceCatalog.FromDefs(new[] { TypeDef() }).Require();
        var instance = new DeviceInstance { Id = DeviceId, TypeId = "conveyor.belt.vspeed" };
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "vv",
            Devices = new() { instance },
            IoMap = new() { new IoTag(DeviceId, "speed", SpeedAddr, new AnalogScale()) },
        };

        var physics = new FakePhysicsWorld();
        var parts = new PartsManager(physics);
        var io = new IoTable(doc, catalog);
        var device = (VariableSpeedConveyor)new DeviceFactory()
            .Also(f => f.Register("conveyor-vspeed", () => new VariableSpeedConveyor()))
            .Create(instance, TypeDef());
        var ctx = new SimContext { Io = io, Physics = physics, Parts = parts, Random = new SeededRandom(1) };
        device.Build(ctx);
        return new Rig { Physics = physics, Io = io, Device = device, Ctx = ctx };
    }

    [Fact]
    public void SetpointZero_EsteiraParada()
    {
        var rig = Build();
        Assert.Equal(0f, rig.SpeedFor(0), 3);
    }

    [Fact]
    public void MeioDaEscala_AndaAMetadeDaVelocidadeMax()
    {
        var rig = Build(); // euMax = 2 m/s
        Assert.Equal(1f, rig.SpeedFor(32768), 2);
    }

    [Fact]
    public void FundoDeEscala_VelocidadeMaxima()
    {
        var rig = Build();
        Assert.Equal(2f, rig.SpeedFor(65535), 2);
    }

    [Fact]
    public void VelocidadeEscalaMonotonicamenteComOSetpoint()
    {
        var rig = Build();
        float v0 = rig.SpeedFor(0);
        float v1 = rig.SpeedFor(16384);
        float v2 = rig.SpeedFor(40000);
        float v3 = rig.SpeedFor(65535);

        Assert.True(v0 < v1 && v1 < v2 && v2 < v3,
            $"esperado monotônico crescente, veio {v0} {v1} {v2} {v3}");
    }

    [Fact]
    public void SetpointNoHash()
    {
        var a = Build();
        var b = Build();
        a.SpeedFor(20000);
        b.SpeedFor(50000);

        var ha = StateHasher.Create();
        a.Device.WriteState(ref ha);
        var hb = StateHasher.Create();
        b.Device.WriteState(ref hb);

        Assert.NotEqual(ha.Hash, hb.Hash);
    }
}
