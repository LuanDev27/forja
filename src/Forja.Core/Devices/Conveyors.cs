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

    /// <summary>Peças dormindo sobre a esteira não acordam sozinhas quando a
    /// superfície estática muda de velocidade — acorda o que está em cima.</summary>
    protected void WakePartsOnBelt(SimContext ctx)
    {
        var center = Instance.Transform.Pos + new Vec3(0f, 0.2f, 0f);
        var half = new Vec3(GetFloat("length", 3f) / 2f, 0.25f, GetFloat("width", 0.5f) / 2f);
        foreach (uint id in ctx.Physics.QueryBox(center, half))
        {
            if (ctx.Parts.TryGet(id, out var part))
                part.Body.Wake();
        }
    }
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

            if (run)
                WakePartsOnBelt(ctx);
        }
    }

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_running);
}

/// <summary>
/// Esteira de velocidade variável (Fase 2, US2): em vez de um bit liga/desliga,
/// lê um SETPOINT de velocidade (m/s) de um holding register (`%QW`) que o CLP
/// escreve. É o primeiro atuador que obedece a um número, não a um estado — a
/// fronteira IoTable converte o bruto do registrador de volta para a unidade de
/// engenharia (ADR 0005). Setpoint 0 = parada; fundo de escala = velocidade máx.
/// </summary>
public sealed class VariableSpeedConveyor : ConveyorBelt
{
    private float _setpoint;

    /// <summary>Setpoint corrente aplicado à superfície, em m/s (só leitura).</summary>
    public float Setpoint => _setpoint;

    public override void Build(SimContext ctx)
    {
        base.Build(ctx);
        _setpoint = 0f;
        Body!.SetSurfaceVelocity(Vec3.Zero);
    }

    public override void Tick(SimContext ctx)
    {
        float sp = ctx.Io.GetOutputWord(Id, "speed"); // EU (m/s), já reescalado do bruto
        if (sp == _setpoint)
            return;

        bool estavaParada = _setpoint == 0f;
        _setpoint = sp;
        Body?.SetSurfaceVelocity(LocalXAxis() * sp);

        // Só precisa acordar peças ao SAIR da parada; mudar de velocidade com a
        // esteira já andando mantém tudo desperto pelo contato.
        if (estavaParada && sp != 0f)
            WakePartsOnBelt(ctx);
    }

    // Grandeza contínua quantizada no hash; o bruto do %QW já entra pela IoTable.
    public override void WriteState(ref StateHasher hasher) => hasher.AddQuantized(_setpoint);
}
