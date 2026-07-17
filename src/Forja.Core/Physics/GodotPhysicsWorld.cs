using Forja.Anvil;
using Godot;

namespace Forja.Core.Physics;

/// <summary>
/// IPhysicsWorld sobre o PhysicsServer3D do Godot (Jolt), 100% via RIDs —
/// sem nós de cena, roda idêntico em godot --headless (T021, Artigo II.3).
/// O espaço vem do World3D da cena principal (gravidade do project settings).
/// A camada visual (Studio) apenas LÊ poses daqui (Artigo II.2).
/// </summary>
public sealed class GodotPhysicsWorld : IPhysicsWorld, IDisposable
{
    private readonly Rid _space;
    private readonly Dictionary<Rid, uint> _entityByBody = new();
    private readonly Rid _queryShape;

    public GodotPhysicsWorld(Rid space)
    {
        _space = space;
        _queryShape = PhysicsServer3D.BoxShapeCreate();
    }

    public IPhysicsBody CreateBox(uint entityId, BodySpec spec)
    {
        var body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, spec.Kind switch
        {
            BodyKind.Static => PhysicsServer3D.BodyMode.Static,
            BodyKind.Kinematic => PhysicsServer3D.BodyMode.Kinematic,
            _ => PhysicsServer3D.BodyMode.Rigid,
        });

        var shape = PhysicsServer3D.BoxShapeCreate();
        PhysicsServer3D.ShapeSetData(shape, ToGodot(spec.HalfExtents));
        PhysicsServer3D.BodyAddShape(body, shape);
        PhysicsServer3D.BodySetSpace(body, _space);
        PhysicsServer3D.BodySetState(
            body, PhysicsServer3D.BodyState.Transform, ToTransform(spec.Pose, spec.TiltXDeg));
        PhysicsServer3D.BodySetParam(body, PhysicsServer3D.BodyParameter.Friction, spec.Friction);
        if (spec.Kind == BodyKind.Rigid)
            PhysicsServer3D.BodySetParam(body, PhysicsServer3D.BodyParameter.Mass, spec.Mass);

        _entityByBody[body] = entityId;
        return new GodotBody(body, shape, spec.TiltXDeg);
    }

    public void Remove(IPhysicsBody body)
    {
        var godotBody = (GodotBody)body;
        _entityByBody.Remove(godotBody.Body);
        godotBody.Free();
    }

    public RayHit? Raycast(Vec3 from, Vec3 to)
    {
        var state = PhysicsServer3D.SpaceGetDirectState(_space);
        var hit = state.IntersectRay(PhysicsRayQueryParameters3D.Create(ToGodot(from), ToGodot(to)));
        if (hit.Count == 0)
            return null;

        var rid = hit["rid"].AsRid();
        if (!_entityByBody.TryGetValue(rid, out uint entityId))
            return null;

        var point = hit["position"].AsVector3();
        return new RayHit(entityId, new Vec3(point.X, point.Y, point.Z));
    }

    public IReadOnlyList<uint> QueryBox(Vec3 center, Vec3 halfExtents)
    {
        PhysicsServer3D.ShapeSetData(_queryShape, ToGodot(halfExtents));
        var state = PhysicsServer3D.SpaceGetDirectState(_space);
        var query = new PhysicsShapeQueryParameters3D
        {
            ShapeRid = _queryShape,
            Transform = new Transform3D(Basis.Identity, ToGodot(center)),
        };

        var results = state.IntersectShape(query, maxResults: 64);
        if (results.Count == 0)
            return Array.Empty<uint>();

        var ids = new List<uint>(results.Count);
        foreach (var entry in results)
        {
            var rid = entry["rid"].AsRid();
            if (_entityByBody.TryGetValue(rid, out uint entityId))
                ids.Add(entityId);
        }

        // A engine não garante ordem — canonicaliza (Artigo I.3).
        ids.Sort();
        return ids;
    }

    public void SetActive(bool active) => PhysicsServer3D.SpaceSetActive(_space, active);

    /// <summary>Libera o shape de query (RIDs não são coletados pelo GC).</summary>
    public void Dispose() => PhysicsServer3D.FreeRid(_queryShape);

    internal static Vector3 ToGodot(Vec3 v) => new(v.X, v.Y, v.Z);

    internal static Transform3D ToTransform(Pose pose, float tiltXDeg)
    {
        var basis = Basis.FromEuler(new Vector3(
            Mathf.DegToRad(tiltXDeg), Mathf.DegToRad(pose.RotY), 0f));
        return new Transform3D(basis, ToGodot(pose.Pos));
    }

    private sealed class GodotBody : IPhysicsBody
    {
        private readonly Rid _shape;
        private readonly float _tiltXDeg;

        public GodotBody(Rid body, Rid shape, float tiltXDeg)
        {
            Body = body;
            _shape = shape;
            _tiltXDeg = tiltXDeg;
        }

        public Rid Body { get; }

        public Pose Pose
        {
            get
            {
                var t = PhysicsServer3D.BodyGetState(
                    Body, PhysicsServer3D.BodyState.Transform).AsTransform3D();
                float rotY = Mathf.RadToDeg(t.Basis.GetEuler().Y);
                return new Pose(new Vec3(t.Origin.X, t.Origin.Y, t.Origin.Z), rotY);
            }
            set => PhysicsServer3D.BodySetState(
                Body, PhysicsServer3D.BodyState.Transform, ToTransform(value, _tiltXDeg));
        }

        public Vec3 LinearVelocity
        {
            get
            {
                var v = PhysicsServer3D.BodyGetState(
                    Body, PhysicsServer3D.BodyState.LinearVelocity).AsVector3();
                return new Vec3(v.X, v.Y, v.Z);
            }
        }

        public bool Asleep =>
            PhysicsServer3D.BodyGetState(Body, PhysicsServer3D.BodyState.Sleeping).AsBool();

        /// <summary>Em corpo estático, LinearVelocity vira velocidade constante
        /// de superfície (mesmo mecanismo do StaticBody3D — esteiras).</summary>
        public void SetSurfaceVelocity(Vec3 velocity) =>
            PhysicsServer3D.BodySetState(
                Body, PhysicsServer3D.BodyState.LinearVelocity, ToGodot(velocity));

        public void Free()
        {
            PhysicsServer3D.FreeRid(Body);
            PhysicsServer3D.FreeRid(_shape);
        }
    }
}
