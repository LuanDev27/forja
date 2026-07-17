using Forja.Core.Physics;

namespace Forja.Core.Devices;

/// <summary>
/// Corpo estático genérico: piso, calha (com tilt), guia lateral, grade
/// estrutural — diferenciados por parâmetros no catálogo (Artigo III.2).
/// </summary>
public sealed class StaticBodyDevice : DeviceBehavior
{
    private IPhysicsBody? _body;

    public override void Build(SimContext ctx)
    {
        _body = ctx.Physics.CreateBox(Id, new BodySpec(
            BodyKind.Static,
            new Anvil.Vec3(GetFloat("sizeX", 1f) / 2f, GetFloat("sizeY", 0.1f) / 2f, GetFloat("sizeZ", 1f) / 2f),
            Instance.Transform,
            TiltXDeg: GetFloat("tilt", 0f),
            Friction: GetFloat("friction", 0.6f)));
    }

    public override void Teardown(SimContext ctx)
    {
        if (_body is not null)
        {
            ctx.Physics.Remove(_body);
            _body = null;
        }
    }

    public override void Tick(SimContext ctx) { }
}
