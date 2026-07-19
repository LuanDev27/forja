using Forja.Anvil;
using Forja.Core.Physics;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>
/// Unidade pick-and-place de dois eixos com garra (spec 002, ADR 0004).
///
/// É um dispositivo COMPOSTO de propósito: internamente tem um cabeçote que se
/// desloca em +X local (horizontal) e −Y (vertical), mais uma garra. Não é uma
/// hierarquia de dispositivos, e por isso não exigiu mexer no schema da cena.
///
/// A garra prende por POSSE, não por atrito: a peça agarrada vira corpo
/// cinemático e passa a ser conduzida pelo cabeçote. É a mesma classe de
/// abstração que a esteira já usa (velocidade de superfície em vez de atrito
/// de correia).
///
/// Portas — ver contracts/pickplace-io.md:
///   Out: advance, lower, grip
///   In:  advanced, retracted, lowered, raised, holding
/// </summary>
public sealed class PickPlace : DeviceBehavior
{
    /// <summary>Meia-altura do cabeçote; também define onde a garra "pega".</summary>
    private const float HeadHalfY = 0.06f;

    private const float EndOfStrokeTolerance = 0.001f;

    private IPhysicsBody? _head;
    private float _extX;
    private float _extY;
    private uint _heldPartId;   // 0 = nenhuma

    /// <summary>Avanço do eixo horizontal, em metros (leitura para o visual).</summary>
    public float ExtensionX => _extX;

    /// <summary>Descida do eixo vertical, em metros (leitura para o visual).</summary>
    public float ExtensionY => _extY;

    /// <summary>Peça presa, ou 0 (leitura para o visual).</summary>
    public uint HeldPartId => _heldPartId;

    public override void Build(SimContext ctx)
    {
        _extX = 0f;
        _extY = 0f;
        _heldPartId = 0;

        _head = ctx.Physics.CreateBox(Id, new BodySpec(
            BodyKind.Kinematic,
            new Vec3(0.06f, HeadHalfY, 0.06f),
            Instance.Transform,
            Friction: 0.4f));
    }

    public override void Teardown(SimContext ctx)
    {
        // FR-009: sair de Run não pode deixar peça cinemática órfã, presa a um
        // dispositivo que deixou de existir.
        Release(ctx);

        if (_head is not null)
        {
            ctx.Physics.Remove(_head);
            _head = null;
        }
    }

    public override void Tick(SimContext ctx)
    {
        float strokeX = GetFloat("strokeX", 0.8f);
        float strokeY = GetFloat("strokeY", 0.4f);

        // --- eixos: mesma rampa do Piston, um por eixo ---
        _extX = Advance(_extX, ctx.Io.GetOutput(Id, "advance") ? strokeX : 0f,
                        GetFloat("speedX", 0.8f));
        _extY = Advance(_extY, ctx.Io.GetOutput(Id, "lower") ? strokeY : 0f,
                        GetFloat("speedY", 0.6f));

        var headPose = HeadPose();
        if (_head is not null)
            _head.Pose = headPose;

        // --- garra ---
        bool grip = ctx.Io.GetOutput(Id, "grip");
        if (grip && _heldPartId == 0)
            TryGrip(ctx, headPose);
        else if (!grip && _heldPartId != 0)
            Release(ctx);

        // Peça presa acompanha o cabeçote. Se ela sumiu no caminho (entrou numa
        // saída, saiu da kill-zone), o vínculo se desfaz sozinho em vez de
        // guardar um id órfão.
        if (_heldPartId != 0)
        {
            if (ctx.Parts.TryGet(_heldPartId, out var held))
                held.Body.Pose = GripPose(headPose);
            else
                _heldPartId = 0;
        }

        // --- fins de curso ---
        // advanced e retracted NÃO são um a negação do outro: no meio do curso
        // os dois são falsos, e é assim que o programa sabe que o eixo está em
        // movimento (contracts/pickplace-io.md).
        ctx.Io.SetInput(Id, "advanced", _extX >= strokeX - EndOfStrokeTolerance);
        ctx.Io.SetInput(Id, "retracted", _extX <= EndOfStrokeTolerance);
        ctx.Io.SetInput(Id, "lowered", _extY >= strokeY - EndOfStrokeTolerance);
        ctx.Io.SetInput(Id, "raised", _extY <= EndOfStrokeTolerance);
        ctx.Io.SetInput(Id, "holding", _heldPartId != 0);
    }

    public override void WriteState(ref StateHasher hasher)
    {
        // Ordem fixa (data-model.md). O id da peça presa entra porque duas
        // execuções agarrando peças DIFERENTES em poses idênticas produziriam
        // estados visualmente iguais — e é justamente isso que o Artigo I.4
        // precisa acusar.
        hasher.AddQuantized(_extX);
        hasher.AddQuantized(_extY);
        hasher.Add(_heldPartId);
    }

    private static float Advance(float current, float target, float speed)
    {
        float delta = Math.Clamp(target - current, -speed * SimContext.Dt, speed * SimContext.Dt);
        return current + delta;
    }

    /// <summary>Pose do cabeçote: avança em +X local, desce em −Y.</summary>
    private Pose HeadPose()
    {
        var basePose = Instance.Transform;
        return basePose with
        {
            Pos = basePose.Pos + LocalXAxis() * _extX + new Vec3(0f, -_extY, 0f),
        };
    }

    /// <summary>Onde a peça presa fica: logo abaixo do cabeçote.</summary>
    private static Pose GripPose(Pose head) =>
        head with { Pos = head.Pos + new Vec3(0f, -HeadHalfY, 0f) };

    private void TryGrip(SimContext ctx, Pose headPose)
    {
        float range = GetFloat("gripRange", 0.12f);
        var center = GripPose(headPose).Pos;
        var ids = ctx.Physics.QueryBox(center, new Vec3(range, range, range));

        // R3: a de MENOR ID entre as candidatas. QueryBox não garante ordem —
        // pegar "a primeira da lista" quebraria o Artigo I.3 de forma
        // esporádica, que é o pior tipo de quebra de determinismo.
        uint best = 0;
        foreach (uint id in ids)
        {
            if (!ctx.Parts.TryGet(id, out _))
                continue;
            if (best == 0 || id < best)
                best = id;
        }

        if (best == 0)
            return;     // garra no vazio: não prende e não trava a sequência

        if (ctx.Parts.TryGet(best, out var part))
        {
            part.Body.SetKind(BodyKind.Kinematic);
            _heldPartId = best;
        }
    }

    private void Release(SimContext ctx)
    {
        if (_heldPartId == 0)
            return;

        if (ctx.Parts.TryGet(_heldPartId, out var part))
        {
            part.Body.SetKind(BodyKind.Rigid);
            // R5: sem isto a peça pode voltar a rígida já adormecida e ficar
            // parada no ar — o mesmo cuidado que a esteira toma ao ligar.
            part.Body.Wake();
        }

        _heldPartId = 0;
    }
}
