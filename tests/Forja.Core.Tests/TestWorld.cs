using System.Linq;
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

    /// <summary>Corpos criados, por entityId — testes conferem o que a esteira
    /// aplicou sem precisar do campo protegido do device.</summary>
    public Dictionary<uint, FakeBody> Bodies { get; } = new();

    public IPhysicsBody CreateBox(uint entityId, BodySpec spec)
    {
        var body = new FakeBody { Pose = spec.Pose };
        Bodies[entityId] = body;
        return body;
    }

    public void Remove(IPhysicsBody body) { }

    /// <summary>O que o próximo Raycast devolve (null = feixe livre). Settável
    /// pelos testes de sensores analógicos/ópticos.</summary>
    public RayHit? RayResult { get; set; }

    public RayHit? Raycast(Vec3 from, Vec3 to) => RayResult;

    /// <summary>O que a próxima QueryBox devolve. A ordem é embaralhada de
    /// propósito na leitura: quem depender dela em vez de ordenar por id
    /// quebra o Artigo I.3, e o teste tem de acusar isso.</summary>
    public List<uint> QueryResult { get; } = new();

    public IReadOnlyList<uint> QueryBox(Vec3 center, Vec3 halfExtents) =>
        QueryResult.Count == 0 ? Array.Empty<uint>() : QueryResult.AsEnumerable().Reverse().ToArray();

    public void SetActive(bool active) => Active = active;

    internal sealed class FakeBody : IPhysicsBody
    {
        public Pose Pose { get; set; }

        public Vec3 LinearVelocity => Vec3.Zero;

        public bool Asleep => false;

        /// <summary>Exposto para os testes conferirem que a garra converteu
        /// o corpo (e o devolveu) — ver ADR 0004.</summary>
        public BodyKind Kind { get; private set; } = BodyKind.Rigid;

        public int WakeCalls { get; private set; }

        /// <summary>Última velocidade de superfície aplicada — testes de esteira
        /// (inclusive a de velocidade variável) conferem por aqui.</summary>
        public Vec3 SurfaceVelocity { get; private set; } = Vec3.Zero;

        public void SetSurfaceVelocity(Vec3 velocity) => SurfaceVelocity = velocity;

        public void Wake() => WakeCalls++;

        public void SetKind(BodyKind kind) => Kind = kind;
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

    public void RaiseRecovered()
    {
        State = DriverState.Ready;
        StateChanged?.Invoke(State, null);
    }

    public IoSnapshot Exchange(IoSnapshot inputs) => IoSnapshot.Empty(inputs.TickNumber);

    public void Dispose() => Stop();
}
