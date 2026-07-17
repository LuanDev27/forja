using Forja.Anvil;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using Forja.Core.Physics;

namespace Forja.Core.Tests;

/// <summary>Catálogo, fábrica e física fake para testes de lógica (Artigo V.1).</summary>
internal static class TestWorld
{
    public static DeviceCatalog Catalog() => DeviceCatalog.FromDefs(new[]
    {
        new DeviceTypeDef
        {
            TypeId = "sensor.test",
            Category = DeviceCategory.Sensor,
            DisplayName = "Sensor de teste",
            Behavior = "noop",
            Ports = new() { new PortDef("detect", IoDirection.In) },
        },
        new DeviceTypeDef
        {
            TypeId = "actuator.test",
            Category = DeviceCategory.Actuator,
            DisplayName = "Atuador de teste",
            Behavior = "noop",
            Ports = new() { new PortDef("extend", IoDirection.Out) },
        },
    }).Require();

    public static DeviceFactory Factory()
    {
        var factory = new DeviceFactory();
        factory.Register("noop", () => new NoopDevice());
        return factory;
    }

    public static SceneDocument Doc(List<DeviceInstance> devices, List<IoTag> ioMap) => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "teste",
        Devices = devices,
        IoMap = ioMap,
    };

    public static SceneDocument SensorAtuadorDoc() => Doc(
        new()
        {
            new DeviceInstance { Id = 1, TypeId = "sensor.test" },
            new DeviceInstance { Id = 2, TypeId = "actuator.test" },
        },
        new()
        {
            new IoTag(1, "detect", new IoAddress(IoArea.DiscreteInput, 0)),
            new IoTag(2, "extend", new IoAddress(IoArea.Coil, 0)),
        });

    private sealed class NoopDevice : DeviceBehavior
    {
        public override void Tick(SimContext ctx) { }
    }
}

/// <summary>Física fake: corpos são bolsas de pose, sem integração.</summary>
internal sealed class FakePhysicsWorld : IPhysicsWorld
{
    public bool Active { get; private set; }

    public IPhysicsBody CreateBox(uint entityId, BodySpec spec) => new FakeBody { Pose = spec.Pose };

    public void Remove(IPhysicsBody body) { }

    public RayHit? Raycast(Vec3 from, Vec3 to) => null;

    public IReadOnlyList<uint> QueryBox(Vec3 center, Vec3 halfExtents) => Array.Empty<uint>();

    public void SetActive(bool active) => Active = active;

    private sealed class FakeBody : IPhysicsBody
    {
        public Pose Pose { get; set; }

        public Vec3 LinearVelocity => Vec3.Zero;

        public bool Asleep => false;

        public void SetSurfaceVelocity(Vec3 velocity) { }
    }
}

/// <summary>Driver fake controlável pelos testes (fault sob demanda).</summary>
internal sealed class FakeDriver : IPlcDriver
{
    public DriverState State { get; private set; } = DriverState.Stopped;

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public event Action<DriverState, string?>? StateChanged;

    public void Start(ConnectionConfig config)
    {
        StartCalls++;
        State = DriverState.Ready;
        StateChanged?.Invoke(State, null);
    }

    public void Stop()
    {
        StopCalls++;
        State = DriverState.Stopped;
    }

    public void RaiseFault(string reason)
    {
        State = DriverState.Faulted;
        StateChanged?.Invoke(State, reason);
    }

    public IoSnapshot Exchange(IoSnapshot inputs) => IoSnapshot.Empty(inputs.TickNumber);

    public void Dispose() => Stop();
}
