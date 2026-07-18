using System;
using System.Collections.Generic;
using System.Text.Json;
using Forja.Anvil;
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
    // Paleta única (mesmos valores dos .tscn em assets/devices — manter em
    // sincronia): estrutura em cinzas frios, acentos dessaturados na mesma
    // faixa de saturação/brilho para nada "gritar" na cena.
    private static readonly Color PlasticColor = new(0.90f, 0.56f, 0.22f); // âmbar
    private static readonly Color MetalColor = new(0.64f, 0.68f, 0.74f);   // aço

    private readonly Main _main;
    private readonly Dictionary<uint, Node3D> _partNodes = new();
    private readonly HashSet<uint> _partsSeen = new();
    private readonly HashSet<uint> _selected = new();

    /// <summary>Caixa lógica de cada dispositivo (o visual não é mais uma
    /// caixa escalada, então o pick precisa da medida guardada à parte).</summary>
    private readonly Dictionary<uint, Vector3> _deviceBounds = new();

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
        _deviceBounds.Clear();

        foreach (var instance in _main.Loop.Document.Devices)
        {
            if (!_main.Catalog.TryGet(instance.TypeId, out var type))
                continue; // cena inválida ainda pode ser exibida parcialmente

            _devicesRoot.AddChild(CreateDeviceVisual(instance, type));
        }

        ApplyHighlight();
    }

    /// <summary>Realça no 3D os dispositivos selecionados no editor (US3).</summary>
    public void SetSelected(IEnumerable<uint> ids)
    {
        _selected.Clear();
        foreach (uint id in ids)
            _selected.Add(id);
        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        foreach (var child in _devicesRoot.GetChildren())
        {
            if (child is Node3D node && TryParseId(node.Name, out uint id)
                && node.GetNodeOrNull("outline") is Node3D outline)
            {
                outline.Visible = _selected.Contains(id);
            }
        }
    }

    /// <summary>Pick por raio no espaço do editor (Edit não tem física — testa
    /// o raio contra a caixa unitária de cada visual, em espaço local).</summary>
    public bool TryPickDevice(Vector3 rayOrigin, Vector3 rayDir, out uint id)
    {
        id = 0;
        float best = float.MaxValue;
        bool hit = false;

        foreach (var child in _devicesRoot.GetChildren())
        {
            if (child is not Node3D node || !TryParseId(node.Name, out uint nid))
                continue;

            if (!_deviceBounds.TryGetValue(nid, out var bounds))
                continue;

            var inv = node.GlobalTransform.AffineInverse();
            var localOrigin = inv * rayOrigin;
            var localDir = inv.Basis * rayDir;
            if (!RayBox(localOrigin, localDir, bounds * 0.5f, out float t))
                continue;

            var worldHit = node.GlobalTransform * (localOrigin + localDir * t);
            float dist = (worldHit - rayOrigin).Length();
            if (dist < best)
            {
                best = dist;
                id = nid;
                hit = true;
            }
        }

        return hit;
    }

    /// <summary>Pose provisória durante arrasto do editor (US3): move só o
    /// visual — o Document é atualizado por comando no fim do gesto.</summary>
    public void PreviewDevicePose(uint id, Pose pose)
    {
        if (_devicesRoot.GetNodeOrNull($"dev-{id}") is not Node3D node)
            return;

        foreach (var instance in _main.Loop.Document.Devices)
        {
            if (instance.Id != id)
                continue;
            if (!_main.Catalog.TryGet(instance.TypeId, out var type))
                return;

            node.Transform = GodotPhysicsWorld.ToTransform(pose, TiltDeg(instance, type));
            return;
        }
    }

    private static bool TryParseId(StringName name, out uint id)
    {
        id = 0;
        string s = name.ToString();
        return s.StartsWith("dev-") && uint.TryParse(s.AsSpan(4), out id);
    }

    /// <summary>Raio contra caixa centrada na origem, em espaço local.</summary>
    private static bool RayBox(Vector3 o, Vector3 d, Vector3 half, out float t)
    {
        t = 0f;
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        for (int i = 0; i < 3; i++)
        {
            float oi = o[i], di = d[i], h = MathF.Max(half[i], 1e-4f);
            if (MathF.Abs(di) < 1e-8f)
            {
                if (oi < -h || oi > h)
                    return false;
            }
            else
            {
                float t1 = (-h - oi) / di, t2 = (h - oi) / di;
                if (t1 > t2)
                    (t1, t2) = (t2, t1);
                tmin = MathF.Max(tmin, t1);
                tmax = MathF.Min(tmax, t2);
                if (tmin > tmax)
                    return false;
            }
        }
        if (tmax < 0f)
            return false;
        t = tmin >= 0f ? tmin : tmax;
        return true;
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

        UpdateIndicatorLights();
    }

    /// <summary>Acende as luzes indicadoras lendo IndicatorLight.On do core
    /// (leitura apenas).</summary>
    private void UpdateIndicatorLights()
    {
        foreach (var device in _main.Loop.Devices)
        {
            if (device is not IndicatorLight light)
                continue;
            if (_devicesRoot.GetNodeOrNull($"dev-{light.Id}") is not Node3D node)
                continue;
            if (LampMaterial(node) is not { } mat)
                continue;

            mat.EmissionEnabled = light.On;
            mat.Emission = light.On ? new Color(0.95f, 0.85f, 0.25f) : Colors.Black;
            mat.AlbedoColor = light.On ? new Color(0.85f, 0.78f, 0.25f) : new Color(0.30f, 0.30f, 0.20f);
        }
    }

    /// <summary>
    /// Material da lâmpada do dispositivo. Tem de ser um material EXCLUSIVO
    /// da instância: os materiais de paleta são compartilhados, e mexer neles
    /// acenderia todas as luzes da cena de uma vez.
    /// </summary>
    private static StandardMaterial3D? LampMaterial(Node3D device)
    {
        var mesh = device.GetNodeOrNull<MeshInstance3D>(DeviceVisuals.LampNode)
                   ?? device.GetNodeOrNull<MeshInstance3D>("body");
        if (mesh is null)
            return null;

        if (mesh.MaterialOverride is StandardMaterial3D over)
            return over;

        return mesh.Mesh?.SurfaceGetMaterial(0) as StandardMaterial3D;
    }

    /// <summary>Tamanho/cor do visual por comportamento. Público porque o
    /// editor (US3) usa o mesmo cálculo no ghost de colocação e no preview.</summary>
    public static (Vector3 Size, Color Color, float Alpha) VisualParams(
        DeviceInstance instance, DeviceTypeDef type)
        => type.Behavior switch
        {
            "static-body" => (SizeParams(instance, type), new Color(0.60f, 0.61f, 0.64f), 1f),
            "conveyor" or "conveyor-io" => (
                new Vector3(
                    GetFloat(instance, type, "length", 3f),
                    0.1f,
                    GetFloat(instance, type, "width", 0.5f)),
                new Color(0.22f, 0.24f, 0.28f), 1f),
            "emitter" => (new Vector3(0.3f, 0.3f, 0.3f), new Color(0.33f, 0.60f, 0.42f), 1f),
            "sink" => (SizeParams(instance, type), new Color(0.75f, 0.32f, 0.30f), 0.40f),
            "push-button" => (new Vector3(0.12f, 0.06f, 0.12f), new Color(0.70f, 0.28f, 0.24f), 1f),
            "selector-switch" => (new Vector3(0.12f, 0.06f, 0.12f), new Color(0.45f, 0.46f, 0.52f), 1f),
            "indicator-light" => (new Vector3(0.12f, 0.12f, 0.12f), new Color(0.30f, 0.30f, 0.20f), 1f),
            _ => (new Vector3(0.3f, 0.3f, 0.3f), new Color(0.45f, 0.46f, 0.50f), 1f),
        };

    /// <summary>Inclinação visual (só static-body tem "tilt") — mesma conversão
    /// usada pela física.</summary>
    public static float TiltDeg(DeviceInstance instance, DeviceTypeDef type) =>
        type.Behavior == "static-body" ? GetFloat(instance, type, "tilt", 0f) : 0f;

    private Node3D CreateDeviceVisual(DeviceInstance instance, DeviceTypeDef type)
    {
        (Vector3 size, Color color, float alpha) = VisualParams(instance, type);
        float tilt = TiltDeg(instance, type);

        // O nó raiz NÃO é escalado: a geometria já nasce no tamanho certo
        // (DeviceVisuals). Escalar o raiz de forma não-uniforme achataria
        // roletes, hastes e qualquer detalhe redondo.
        var node = DeviceVisuals.Build(instance, type, size, color, alpha);
        node.Name = $"dev-{instance.Id}";
        node.Transform = GodotPhysicsWorld.ToTransform(instance.Transform, tilt);
        _deviceBounds[instance.Id] = size;

        // Contorno de seleção (oculto por padrão) — caixa lógica um pouco maior.
        node.AddChild(new MeshInstance3D
        {
            Name = "outline",
            Mesh = new BoxMesh { Size = size * 1.06f },
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.2f, 0.30f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Front,
            },
        });

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
