using System.Collections.Generic;
using System.Text.Json;
using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using Forja.Core.Physics;
using Godot;

namespace Forja.Studio.Rendering;

/// <summary>
/// Camada visual (T023, Artigo II.2): LÊ Document/Parts do core e desenha —
/// nunca escreve estado de simulação. Dispositivos são reconstruídos quando o
/// modo muda (via CallDeferred: o sinal dispara dentro do tick de física);
/// peças são sincronizadas todo frame em _Process. Usa a MESMA conversão
/// Pose→Transform3D da física (GodotPhysicsWorld.ToTransform), então o
/// desenho corresponde exatamente ao que a física vê.
/// </summary>
public partial class SceneView : Node3D
{
    private static readonly Color PlasticColor = new(0.90f, 0.45f, 0.10f); // laranja
    private static readonly Color MetalColor = new(0.62f, 0.66f, 0.72f);   // aço

    private readonly Main _main;
    private readonly Dictionary<uint, Node3D> _partNodes = new();
    private readonly HashSet<uint> _partsSeen = new();

    private Node3D _devicesRoot = null!;
    private Node3D _partsRoot = null!;
    private StandardMaterial3D? _plasticMat;
    private StandardMaterial3D? _metalMat;

    public SceneView(Main main) => _main = main;

    public override void _Ready()
    {
        _devicesRoot = new Node3D { Name = "Devices" };
        _partsRoot = new Node3D { Name = "Parts" };
        AddChild(_devicesRoot);
        AddChild(_partsRoot);

        _main.Loop.ModeChanged += _ => Callable.From(Rebuild).CallDeferred();
        Rebuild();
    }

    /// <summary>Reconstrói os visuais dos dispositivos a partir do Document.
    /// Também serve ao editor (US3) após alterar a cena em Edit.</summary>
    public void Rebuild()
    {
        foreach (var child in _devicesRoot.GetChildren())
            child.Free();

        foreach (var instance in _main.Loop.Document.Devices)
        {
            if (!_main.Catalog.TryGet(instance.TypeId, out var type))
                continue; // cena inválida ainda pode ser exibida parcialmente

            _devicesRoot.AddChild(CreateDeviceVisual(instance, type));
        }
    }

    public override void _Process(double delta)
    {
        _partsSeen.Clear();

        if (_main.Loop.Parts is { } parts)
        {
            foreach (var part in parts.All)
            {
                _partsSeen.Add(part.Id);
                if (!_partNodes.TryGetValue(part.Id, out var node))
                {
                    node = CreatePartVisual(part);
                    _partNodes[part.Id] = node;
                    _partsRoot.AddChild(node);
                }

                // Pose só expõe pos+rotY (estado canônico do core) — tombos
                // em X/Z não aparecem; aproximação aceita da camada visual.
                node.Transform = GodotPhysicsWorld.ToTransform(part.Body.Pose, 0f);
                node.Scale = GodotPhysicsWorld.ToGodot(part.Kind.HalfExtents) * 2f;
            }
        }

        List<uint>? gone = null;
        foreach (var (id, node) in _partNodes)
        {
            if (!_partsSeen.Contains(id))
            {
                node.QueueFree();
                (gone ??= new List<uint>()).Add(id);
            }
        }
        if (gone is not null)
        {
            foreach (uint id in gone)
                _partNodes.Remove(id);
        }
    }

    private static Node3D CreateDeviceVisual(DeviceInstance instance, DeviceTypeDef type)
    {
        (Vector3 size, Color color, float alpha) = type.Behavior switch
        {
            "static-body" => (SizeParams(instance, type), new Color(0.55f, 0.56f, 0.58f), 1f),
            "conveyor" or "conveyor-io" => (
                new Vector3(
                    GetFloat(instance, type, "length", 3f),
                    0.1f,
                    GetFloat(instance, type, "width", 0.5f)),
                new Color(0.20f, 0.22f, 0.25f), 1f),
            "emitter" => (new Vector3(0.3f, 0.3f, 0.3f), new Color(0.15f, 0.70f, 0.25f), 1f),
            "sink" => (SizeParams(instance, type), new Color(0.80f, 0.15f, 0.15f), 0.35f),
            _ => (new Vector3(0.3f, 0.3f, 0.3f), new Color(0.40f, 0.40f, 0.45f), 1f),
        };

        float tilt = type.Behavior == "static-body" ? GetFloat(instance, type, "tilt", 0f) : 0f;

        var node = InstantiateVisual(type.VisualScene, color, alpha);
        node.Name = $"dev-{instance.Id}";
        node.Transform = GodotPhysicsWorld.ToTransform(instance.Transform, tilt);
        node.Scale = size;
        return node;
    }

    private Node3D CreatePartVisual(Part part)
    {
        var material = part.Kind.Material == "metal"
            ? _metalMat ??= MakeMaterial(MetalColor, 1f)
            : _plasticMat ??= MakeMaterial(PlasticColor, 1f);

        var node = InstantiateVisual("res://assets/devices/part.box.tscn", PlasticColor, 1f);
        if (node is MeshInstance3D mesh)
            mesh.MaterialOverride = material;
        node.Name = $"part-{part.Id}";
        return node;
    }

    /// <summary>Instancia a cena do catálogo se existir; senão caixa unitária
    /// procedural. Nos dois casos o tamanho real entra via Scale.</summary>
    private static Node3D InstantiateVisual(string visualScene, Color color, float alpha)
    {
        if (!string.IsNullOrEmpty(visualScene) && ResourceLoader.Exists(visualScene))
        {
            var packed = ResourceLoader.Load<PackedScene>(visualScene);
            if (packed?.Instantiate() is Node3D fromScene)
                return fromScene;
        }

        return new MeshInstance3D
        {
            Mesh = new BoxMesh(),
            MaterialOverride = MakeMaterial(color, alpha),
        };
    }

    private static StandardMaterial3D MakeMaterial(Color color, float alpha)
    {
        color.A = alpha;
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = alpha < 1f
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
        };
    }

    private static Vector3 SizeParams(DeviceInstance instance, DeviceTypeDef type) => new(
        GetFloat(instance, type, "sizeX", 1f),
        GetFloat(instance, type, "sizeY", 0.1f),
        GetFloat(instance, type, "sizeZ", 1f));

    /// <summary>Parâmetro da instância com fallback no default do catálogo —
    /// mesma resolução do DeviceBehavior, replicada porque a camada 4 não
    /// enxerga behaviors instanciados em Edit.</summary>
    private static float GetFloat(DeviceInstance instance, DeviceTypeDef type, string name, float fallback)
    {
        if (instance.Params.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.Number)
            return el.GetSingle();

        foreach (var def in type.ParamDefs)
        {
            if (def.Name == name && def.Default is { ValueKind: JsonValueKind.Number } dflt)
                return dflt.GetSingle();
        }
        return fallback;
    }
}
