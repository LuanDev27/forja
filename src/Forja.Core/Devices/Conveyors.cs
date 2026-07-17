using Forja.Anvil;
using Forja.Core.Physics;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>
/// Esteira reta (RF-03): corpo estático com velocidade de superfície — a
/// fricção tangencial move as peças em contato (RF-04).
/// </summary>
public class ConveyorBelt : DeviceBehavior
{
    protected IPhysicsBody? Body;

    protected float Speed => GetFloat("speed", 0.5f) * (GetBool("reversed") ? -1f : 1f);

    public override void Build(SimContext ctx)
    {
        Body = ctx.Physics.CreateBox(Id, new BodySpec(
            BodyKind.Static,
            new Vec3(GetFloat("length", 3f) / 2f, 0.05f, GetFloat("width", 0.5f) / 2f),
            Instance.Transform,
            Friction: 1.0f));
        Body.SetSurfaceVelocity(LocalXAxis() * Speed);
    }

    public override void Teardown(SimContext ctx)
    {
        if (Body is not null)
        {
            ctx.Physics.Remove(Body);
            Body = null;
        }
    }

    public override void Tick(SimContext ctx) { }
}

/// <summary>Esteira acionada por I/O: liga/desliga pelo bit de saída "run".</summary>
public sealed class ConveyorBeltIo : ConveyorBelt
{
    private bool _running;

    public override void Build(SimContext ctx)
    {
        base.Build(ctx);
        _running = false;
        Body!.SetSurfaceVelocity(Vec3.Zero);
    }

    public override void Tick(SimContext ctx)
    {
        bool run = ctx.Io.GetOutput(Id, "run");
        if (run != _running)
        {
            _running = run;
            Body?.SetSurfaceVelocity(run ? LocalXAxis() * Speed : Vec3.Zero);
        }
    }

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_running);
}
