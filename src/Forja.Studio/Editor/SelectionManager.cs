using System.Linq;
using Forja.Anvil;
using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Core.Editing;
using Forja.Core.Physics;
using Forja.Studio.Rendering;
using Godot;

namespace Forja.Studio.Editor;

/// <summary>
/// Roteador único de entrada do editor (T038). Clique esquerdo: coloca o tipo
/// armado, ou gira pelo anel do RotateGizmo, ou seleciona (Ctrl acumula) e
/// arrasta no plano do chão com snap de 0,1 m; rotação com snap de 15°.
/// Teclas: Delete remove, Ctrl+D duplica, Ctrl+Z/Ctrl+Y desfaz/refaz, Q/E
/// giram ±15°, PageUp/PageDown sobem/descem 0,1 m, Esc cancela colocação.
/// Arrasto mexe só no visual (preview); o Document muda por comando ao soltar.
/// </summary>
public partial class SelectionManager : Node3D
{
    public const float GridSnap = 0.1f;
    public const float AngleSnap = 15f;

    private enum DragKind { None, Move, Rotate }

    private readonly EditorContext _ctx;
    private MoveGizmo _move = null!;
    private RotateGizmo _rotate = null!;
    private MeshInstance3D _ghost = null!;

    private DragKind _drag = DragKind.None;
    private uint _dragId;
    private Pose _dragStart;
    private Pose _dragCurrent;
    private Vector3 _grabOffset;
    private float _grabAngle;

    public SelectionManager(EditorContext ctx) => _ctx = ctx;

    public override void _Ready()
    {
        _move = new MoveGizmo { Name = "MoveGizmo" };
        _rotate = new RotateGizmo { Name = "RotateGizmo" };
        AddChild(_move);
        AddChild(_rotate);

        _ghost = new MeshInstance3D
        {
            Name = "PlacementGhost",
            Mesh = new BoxMesh(),
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.40f, 0.75f, 0.50f, 0.35f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        AddChild(_ghost);
    }

    public override void _Process(double delta)
    {
        bool edit = _ctx.InEdit;
        if (!edit && _drag != DragKind.None)
            _drag = DragKind.None; // trocar de modo no meio do gesto cancela

        Pose? pose = _drag != DragKind.None ? _dragCurrent : ActiveDevicePose();
        bool show = edit && pose is not null;
        _move.Visible = show;
        _rotate.Visible = show;
        if (pose is { } p)
        {
            var origin = GodotPhysicsWorld.ToGodot(p.Pos);
            _move.Position = origin;
            _rotate.Position = origin;
        }

        _ghost.Visible = edit && _ctx.ArmedTypeId is not null && _drag == DragKind.None;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_ctx.InEdit)
            return;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                OnPress(mb);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } :
                OnRelease();
                break;
            case InputEventMouseMotion mm:
                OnMotion(mm.Position);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                OnKey(key);
                break;
        }
    }

    private void OnPress(InputEventMouseButton mb)
    {
        if (!TryMouseRay(mb.Position, out var origin, out var dir))
            return;

        if (_ctx.ArmedTypeId is { } typeId)
        {
            PlaceArmed(typeId, origin, dir, keepArmed: mb.ShiftPressed);
            return;
        }

        if (_ctx.ActiveId is { } activeId && ActiveDevicePose() is { } activePose
            && _rotate.HitsRing(origin, dir, GodotPhysicsWorld.ToGodot(activePose.Pos), out float angle))
        {
            _drag = DragKind.Rotate;
            _dragId = activeId;
            _dragStart = _dragCurrent = activePose;
            _grabAngle = angle;
            return;
        }

        if (_ctx.View.TryPickDevice(origin, dir, out uint id))
        {
            bool ctrl = mb.CtrlPressed;
            _ctx.Select(id, ctrl);
            if (!ctrl && _ctx.ActiveId == id && DevicePose(id) is { } pose
                && PlaneHit(origin, dir, pose.Pos.Y) is { } hit)
            {
                _drag = DragKind.Move;
                _dragId = id;
                _dragStart = _dragCurrent = pose;
                _grabOffset = GodotPhysicsWorld.ToGodot(pose.Pos) - hit;
            }
            return;
        }

        if (!mb.CtrlPressed)
            _ctx.ClearSelection();
    }

    private void OnMotion(Vector2 mouse)
    {
        if (_drag == DragKind.None)
        {
            UpdateGhost(mouse);
            return;
        }

        if (!TryMouseRay(mouse, out var origin, out var dir))
            return;

        if (_drag == DragKind.Move && PlaneHit(origin, dir, _dragStart.Pos.Y) is { } hit)
        {
            var target = hit + _grabOffset;
            _dragCurrent = _dragStart with
            {
                Pos = new Vec3(Snap(target.X), _dragStart.Pos.Y, Snap(target.Z)),
            };
            _ctx.View.PreviewDevicePose(_dragId, _dragCurrent);
        }
        else if (_drag == DragKind.Rotate && _rotate.TryAngle(
            origin, dir, GodotPhysicsWorld.ToGodot(_dragStart.Pos), requireRing: false, out float angle))
        {
            float delta = Mathf.Wrap(angle - _grabAngle, -180f, 180f);
            _dragCurrent = _dragStart with { RotY = SnapAngle(_dragStart.RotY + delta) };
            _ctx.View.PreviewDevicePose(_dragId, _dragCurrent);
        }
    }

    private void OnRelease()
    {
        if (_drag == DragKind.Move && _dragCurrent != _dragStart)
            _ctx.Execute(new MoveDeviceCommand(_dragId, _dragCurrent.Pos));
        else if (_drag == DragKind.Rotate && _dragCurrent != _dragStart)
            _ctx.Execute(new RotateDeviceCommand(_dragId, _dragCurrent.RotY));
        else if (_drag != DragKind.None)
            _ctx.View.PreviewDevicePose(_dragId, _dragStart); // gesto sem efeito

        _drag = DragKind.None;
    }

    private void OnKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Escape:
                _ctx.ArmedTypeId = null;
                break;
            case Key.Delete when _ctx.Selection.Count > 0:
                _ctx.Execute(new DeleteSelectionCommand(_ctx.Selection));
                break;
            case Key.D when key.CtrlPressed && _ctx.Selection.Count > 0:
                DuplicateSelection();
                break;
            case Key.Z when key.CtrlPressed && key.ShiftPressed:
                _ctx.Redo();
                break;
            case Key.Z when key.CtrlPressed:
                _ctx.Undo();
                break;
            case Key.Y when key.CtrlPressed:
                _ctx.Redo();
                break;
            case Key.Q when ActiveDevicePose() is { } pose:
                RotateActive(pose, +AngleSnap);
                break;
            case Key.E when ActiveDevicePose() is { } pose:
                RotateActive(pose, -AngleSnap);
                break;
            case Key.Pageup when ActiveDevicePose() is { } pose:
                NudgeY(pose, +GridSnap);
                break;
            case Key.Pagedown when ActiveDevicePose() is { } pose:
                NudgeY(pose, -GridSnap);
                break;
        }
    }

    private void PlaceArmed(string typeId, Vector3 origin, Vector3 dir, bool keepArmed)
    {
        if (!_ctx.Main.Catalog.TryGet(typeId, out var type)
            || PlaneHit(origin, dir, 0f) is not { } ground)
        {
            return;
        }

        float centerY = PlacementCenterY(type, GhostSize(typeId, type));
        var pos = new Vec3(Snap(ground.X), centerY, Snap(ground.Z));

        if (_ctx.Execute(new PlaceDeviceCommand(typeId, new Pose(pos, 0f))))
        {
            var doc = _ctx.Main.Loop.Document;
            _ctx.SelectExactly(new[] { doc.Devices[^1].Id });
        }

        if (!keepArmed)
            _ctx.ArmedTypeId = null;
    }

    private void DuplicateSelection()
    {
        uint firstNewId = _ctx.Main.Loop.Document.NextDeviceId();
        var offset = new Vec3(2f * GridSnap, 0f, 2f * GridSnap);
        if (_ctx.Execute(new DuplicateSelectionCommand(_ctx.Selection, offset)))
        {
            _ctx.SelectExactly(_ctx.Main.Loop.Document.Devices
                .Where(d => d.Id >= firstNewId)
                .Select(d => d.Id));
        }
    }

    private void RotateActive(Pose pose, float deltaDeg)
    {
        if (_ctx.ActiveId is { } id)
            _ctx.Execute(new RotateDeviceCommand(id, SnapAngle(pose.RotY + deltaDeg)));
    }

    private void NudgeY(Pose pose, float deltaY)
    {
        if (_ctx.ActiveId is { } id)
            _ctx.Execute(new MoveDeviceCommand(id, pose.Pos + new Vec3(0f, deltaY, 0f)));
    }

    private void UpdateGhost(Vector2 mouse)
    {
        if (_ctx.ArmedTypeId is not { } typeId
            || !_ctx.Main.Catalog.TryGet(typeId, out var type)
            || !TryMouseRay(mouse, out var origin, out var dir)
            || PlaneHit(origin, dir, 0f) is not { } ground)
        {
            return;
        }

        var size = GhostSize(typeId, type);
        _ghost.Position = new Vector3(Snap(ground.X), PlacementCenterY(type, size), Snap(ground.Z));
        _ghost.Scale = size;
    }

    /// <summary>Base no chão, ou topo no chão para tipos flushToGround
    /// (pisos): assim o piso nunca engole o que está apoiado sobre ele.</summary>
    private static float PlacementCenterY(DeviceTypeDef type, Vector3 size) =>
        type.FlushToGround ? -size.Y / 2f : size.Y / 2f;

    private static Vector3 GhostSize(string typeId, DeviceTypeDef type)
    {
        // Instância vazia → VisualParams resolve pelos defaults do catálogo.
        var blank = new DeviceInstance { Id = 0, TypeId = typeId };
        return SceneView.VisualParams(blank, type).Size;
    }

    private Pose? ActiveDevicePose() => _ctx.ActiveId is { } id ? DevicePose(id) : null;

    private Pose? DevicePose(uint id)
    {
        foreach (var device in _ctx.Main.Loop.Document.Devices)
        {
            if (device.Id == id)
                return device.Transform;
        }
        return null;
    }

    private bool TryMouseRay(Vector2 mouse, out Vector3 origin, out Vector3 dir)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null)
        {
            origin = dir = Vector3.Zero;
            return false;
        }

        origin = camera.ProjectRayOrigin(mouse);
        dir = camera.ProjectRayNormal(mouse);
        return true;
    }

    private static Vector3? PlaneHit(Vector3 origin, Vector3 dir, float y) =>
        new Plane(Vector3.Up, y).IntersectsRay(origin, dir);

    private static float Snap(float v) => Mathf.Round(v / GridSnap) * GridSnap;

    private static float SnapAngle(float deg) =>
        Mathf.Wrap(Mathf.Round(deg / AngleSnap) * AngleSnap, -180f, 180f);
}
