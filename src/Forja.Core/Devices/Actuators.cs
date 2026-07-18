using Forja.Anvil;
using Forja.Core.Physics;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>
/// Pistão pneumático / desviador (RF-03): corpo cinemático que avança ao
/// longo do eixo local +X enquanto o bit de saída está ativo, empurrando
/// peças (RF-04). Porta "extend" (Out); porta opcional "extended" (In) — o
/// pusher do catálogo simplesmente não a declara.
/// </summary>
public sealed class Piston : DeviceBehavior
{
    private IPhysicsBody? _rod;
    private float _extension;

    /// <summary>Avanço atual da haste em metros — a camada visual desenha a
    /// haste onde a física a colocou (só leitura, Artigo II.2).</summary>
    public float Extension => _extension;

    public override void Build(SimContext ctx)
    {
        _extension = 0f;
        _rod = ctx.Physics.CreateBox(Id, new BodySpec(
            BodyKind.Kinematic,
            new Vec3(GetFloat("rodLength", 0.3f) / 2f, 0.1f, GetFloat("rodWidth", 0.3f) / 2f),
            Instance.Transform,
            Friction: 0.4f));
    }

    public override void Teardown(SimContext ctx)
    {
        if (_rod is not null)
        {
            ctx.Physics.Remove(_rod);
            _rod = null;
        }
    }

    public override void Tick(SimContext ctx)
    {
        float stroke = GetFloat("stroke", 0.6f);
        float speed = GetFloat("speed", 1.5f);

        float target = ctx.Io.GetOutput(Id, "extend") ? stroke : 0f;
        float delta = Math.Clamp(target - _extension, -speed * SimContext.Dt, speed * SimContext.Dt);
        _extension += delta;

        if (_rod is not null)
        {
            var basePose = Instance.Transform;
            _rod.Pose = basePose with { Pos = basePose.Pos + LocalXAxis() * _extension };
        }

        // Fim de curso (a porta pode não existir no tipo — SetInput é no-op).
        ctx.Io.SetInput(Id, "extended", _extension >= stroke - 0.001f);
    }

    public override void WriteState(ref StateHasher hasher) => hasher.AddQuantized(_extension);
}

/// <summary>
/// Stopper (trava de esteira): barreira cinemática que sobe quando o bit
/// "close" está ativo, segurando as peças; desce para liberar.
/// </summary>
public sealed class Stopper : DeviceBehavior
{
    private const float DropDepth = 0.35f;

    private IPhysicsBody? _gate;
    private float _raise; // 0 = aberto (embaixo), 1 = fechado (na posição)

    /// <summary>Quanto a trava está levantada, 0..1 (só leitura para o visual).</summary>
    public float Raise => _raise;

    /// <summary>Curso vertical da trava, em metros (o visual desce o mesmo).</summary>
    public static float Drop => DropDepth;

    public override void Build(SimContext ctx)
    {
        _raise = 0f;
        _gate = ctx.Physics.CreateBox(Id, new BodySpec(
            BodyKind.Kinematic,
            new Vec3(0.05f, 0.15f, GetFloat("width", 0.5f) / 2f),
            LoweredPose(),
            Friction: 0.4f));
    }

    public override void Teardown(SimContext ctx)
    {
        if (_gate is not null)
        {
            ctx.Physics.Remove(_gate);
            _gate = null;
        }
    }

    public override void Tick(SimContext ctx)
    {
        float target = ctx.Io.GetOutput(Id, "close") ? 1f : 0f;
        float speed = 4f; // ciclos rápidos, curso curto
        _raise = Math.Clamp(_raise + Math.Clamp(target - _raise, -1f, 1f) * speed * SimContext.Dt, 0f, 1f);

        if (_gate is not null)
        {
            var pose = Instance.Transform;
            _gate.Pose = pose with
            {
                Pos = pose.Pos + new Vec3(0, -DropDepth * (1f - _raise), 0),
            };
        }
    }

    private Pose LoweredPose()
    {
        var pose = Instance.Transform;
        return pose with { Pos = pose.Pos + new Vec3(0, -DropDepth, 0) };
    }

    public override void WriteState(ref StateHasher hasher) => hasher.AddQuantized(_raise);
}
