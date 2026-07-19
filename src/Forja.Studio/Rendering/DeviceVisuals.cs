using System.Text.Json;
using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Godot;

namespace Forja.Studio.Rendering;

/// <summary>
/// Constrói o visual de cada tipo de dispositivo com a geometria já no
/// tamanho certo (Artigo II.2: só desenha, nunca escreve estado).
///
/// Por que construir em código em vez de escalar uma cena pronta: o visual
/// antigo era UMA caixa unitária escalada de forma não-uniforme pelos
/// parâmetros — com isso qualquer detalhe (rolete, perfil, haste) sairia
/// esmagado junto. Aqui cada peça nasce com a medida real, então uma esteira
/// de 4 m tem roletes redondos iguais aos de uma de 1 m.
///
/// A caixa lógica (VisualParams.Size) continua sendo a referência de seleção
/// e colisão do editor — o desenho vive dentro dela. As partes que se movem
/// (haste, trava) saem dessa caixa em tempo de execução, de propósito: elas
/// acompanham o corpo cinemático que a física de fato move.
/// </summary>
public static class DeviceVisuals
{
    /// <summary>Nome do nó da lâmpada; SceneView acende lendo o core.</summary>
    public const string LampNode = "lamp";

    /// <summary>Nome do nó que se move com o atuador (haste do pistão).</summary>
    public const string RodNode = "rod";

    /// <summary>Nome do nó da trava do stopper (sobe e desce).</summary>
    public const string GateNode = "gate";

    /// <summary>Nome do nó da lente do sensor (acende ao detectar).</summary>
    public const string LensNode = "lens";

    // Paleta industrial: estrutura em cinza claro, superfícies de trabalho em
    // borracha escura, metal só onde há peça girando ou deslizando.
    private static readonly Color FrameColor = new(0.62f, 0.64f, 0.67f);
    private static readonly Color RubberColor = new(0.16f, 0.17f, 0.19f);
    private static readonly Color SteelColor = new(0.72f, 0.74f, 0.78f);
    private static readonly Color AccentColor = new(0.85f, 0.65f, 0.15f);
    private static readonly Color ChromeColor = new(0.86f, 0.87f, 0.90f);
    private static readonly Color HousingColor = new(0.20f, 0.21f, 0.24f);
    private static readonly Color ButtonColor = new(0.66f, 0.16f, 0.13f);

    private static StandardMaterial3D? _frame;
    private static StandardMaterial3D? _rubber;
    private static StandardMaterial3D? _steel;
    private static StandardMaterial3D? _accent;
    private static StandardMaterial3D? _chrome;
    private static StandardMaterial3D? _housing;
    private static StandardMaterial3D? _button;

    private static StandardMaterial3D Frame => _frame ??= Metal(FrameColor, 0.35f, 0.55f);
    private static StandardMaterial3D Rubber => _rubber ??= Metal(RubberColor, 0.0f, 0.85f);
    private static StandardMaterial3D Steel => _steel ??= Metal(SteelColor, 0.85f, 0.25f);
    private static StandardMaterial3D Accent => _accent ??= Metal(AccentColor, 0.2f, 0.5f);
    /// <summary>Haste cromada: metálica o bastante para brilhar, mas não tão
    /// espelhada a ponto de virar um bloco preto quando o céu está escuro.</summary>
    private static StandardMaterial3D Chrome => _chrome ??= Metal(ChromeColor, 0.5f, 0.12f);
    private static StandardMaterial3D Housing => _housing ??= Metal(HousingColor, 0.1f, 0.6f);
    private static StandardMaterial3D Button => _button ??= Metal(ButtonColor, 0.1f, 0.45f);

    /// <summary>
    /// Visual do dispositivo, já dimensionado. <paramref name="size"/> é a
    /// caixa lógica do tipo e <paramref name="groundY"/> a altura do
    /// dispositivo no mundo (para os pés alcançarem o chão).
    /// </summary>
    public static Node3D Build(
        DeviceInstance instance,
        DeviceTypeDef type,
        Vector3 size,
        Color fallbackColor,
        float alpha)
    {
        var root = new Node3D();
        float standHeight = instance.Transform.Pos.Y;

        switch (type.Behavior)
        {
            case "conveyor":
            case "conveyor-io":
                BuildConveyor(root, size.X, size.Z, standHeight);
                break;

            case "piston":
                BuildPiston(root, size.X, size.Z, GetFloat(instance, type, "stroke", 0.6f));
                break;

            case "stopper":
                BuildStopper(root, size.Z);
                break;

            case "photo-sensor":
            case "proximity-sensor":
                BuildSensor(root, Vector3.Right);
                break;

            case "height-sensor":
                BuildSensor(root, Vector3.Down);
                break;

            case "indicator-light":
                BuildIndicatorLight(root);
                break;

            case "push-button":
                BuildPushButton(root);
                break;

            case "selector-switch":
                BuildSelectorSwitch(root);
                break;

            case "emitter":
                BuildEmitter(root);
                break;

            case "sink":
                BuildSink(root, size, fallbackColor, alpha);
                break;

            case "static-body":
                BuildStaticBody(root, type.TypeId, size, fallbackColor);
                break;

            default:
                // Tipo ainda não modelado: caixa da paleta antiga, para a cena
                // continuar legível enquanto os visuais são migrados.
                root.AddChild(BoxMesh("body", size, Vector3.Zero, Flat(fallbackColor, alpha)));
                break;
        }

        return root;
    }

    /// <summary>
    /// Esteira: correia de borracha na altura exata da superfície física
    /// (topo em +0,05, igual às meias-extensões do corpo), perfis laterais,
    /// tambores nas pontas e pés até o chão.
    /// </summary>
    private static void BuildConveyor(Node3D root, float length, float width, float standHeight)
    {
        const float halfH = 0.05f;      // meia-altura do corpo físico da esteira
        const float beltThickness = 0.05f;
        float top = halfH;

        // Correia: o topo tem de coincidir com a superfície que move as peças.
        root.AddChild(BoxMesh(
            "belt",
            new Vector3(length - 0.1f, beltThickness, width),
            new Vector3(0f, top - beltThickness / 2f, 0f),
            Rubber));

        // Perfis laterais, um pouco abaixo do nível da correia.
        float sideZ = width / 2f + 0.03f;
        var sideSize = new Vector3(length, 0.09f, 0.06f);
        root.AddChild(BoxMesh("side-a", sideSize, new Vector3(0f, top - 0.055f, sideZ), Frame));
        root.AddChild(BoxMesh("side-b", sideSize, new Vector3(0f, top - 0.055f, -sideZ), Frame));

        // Tambores das pontas: é o que faz a peça "ler" como esteira de lado.
        float drumX = length / 2f - 0.05f;
        float drumY = top - beltThickness / 2f;
        root.AddChild(Roller("drum-a", width + 0.02f, 0.05f, new Vector3(drumX, drumY, 0f), Steel));
        root.AddChild(Roller("drum-b", width + 0.02f, 0.05f, new Vector3(-drumX, drumY, 0f), Steel));

        // Roletes de apoio visíveis no vão entre a correia e o perfil.
        int rollers = Mathf.Clamp((int)(length / 0.35f), 2, 24);
        float step = (length - 0.3f) / Mathf.Max(1, rollers - 1);
        for (int i = 0; i < rollers; i++)
        {
            float x = -(length - 0.3f) / 2f + i * step;
            root.AddChild(Roller($"roller-{i}", width, 0.035f,
                new Vector3(x, top - beltThickness - 0.02f, 0f), Steel));
        }

        // Pés: só quando o equipamento está elevado sobre o piso.
        float legLength = standHeight - halfH;
        if (legLength > 0.05f)
        {
            float legX = length / 2f - 0.12f;
            float legY = top - halfH - legLength / 2f - 0.04f;
            var legSize = new Vector3(0.05f, legLength, 0.05f);
            foreach (int sx in new[] { -1, 1 })
            {
                foreach (int sz in new[] { -1, 1 })
                {
                    root.AddChild(BoxMesh(
                        $"leg-{sx}{sz}",
                        legSize,
                        new Vector3(sx * legX, legY, sz * (width / 2f - 0.02f)),
                        Frame));
                }
            }
        }
    }

    /// <summary>
    /// Pistão pneumático: a haste ("rod") é EXATAMENTE o corpo cinemático que
    /// a física move — mesmas medidas, mesma origem — e SceneView a desloca
    /// em +X local por Piston.Extension. O cilindro fica atrás, recuado o
    /// bastante para a haste nunca sair dele quando recolhida.
    /// </summary>
    private static void BuildPiston(Node3D root, float rodLength, float rodWidth, float stroke)
    {
        // Meia-altura 0,1 é a do corpo físico (Piston.Build) — a haste tem de
        // empurrar a peça na mesma faixa de altura em que a física empurra.
        root.AddChild(BoxMesh(
            RodNode,
            new Vector3(rodLength, 0.2f, rodWidth),
            Vector3.Zero,
            Chrome));

        // Cilindro: comprimento suficiente para engolir o curso inteiro.
        float barrelLength = stroke + 0.16f;
        float barrelRadius = Mathf.Max(rodWidth, 0.2f) / 2f;
        float barrelX = -(rodLength / 2f + barrelLength / 2f);
        root.AddChild(Cylinder("barrel", barrelLength, barrelRadius,
            new Vector3(barrelX, 0f, 0f), Axis.X, Frame));

        // Tampas das pontas e as duas tirantes: é o que faz ler como atuador
        // pneumático e não como um tubo qualquer. Tampa é DISCO, não placa:
        // uma placa quadrada em volta de um tubo redondo projeta os cantos
        // para fora e o pistão ganha "aletas" que não existem.
        foreach (int sx in new[] { -1, 1 })
        {
            root.AddChild(Cylinder(
                $"cap-{(sx < 0 ? "rear" : "front")}",
                0.03f,
                barrelRadius * 1.12f,
                new Vector3(barrelX + sx * barrelLength / 2f, 0f, 0f),
                Axis.X,
                Accent));
        }

        foreach (int sz in new[] { -1, 1 })
        {
            root.AddChild(Cylinder($"tie-{sz}", barrelLength, 0.012f,
                new Vector3(barrelX, barrelRadius * 0.7f, sz * barrelRadius * 0.7f),
                Axis.X, Steel));
        }
    }

    /// <summary>
    /// Stopper: a trava ("gate") tem as medidas do corpo cinemático de
    /// Stopper.Build e é desenhada na posição LEVANTADA. SceneView a abaixa
    /// por Stopper.Drop × (1 − Raise), igual à física. O berço fica fixo,
    /// abaixo do curso, para a trava ter de onde sair.
    /// </summary>
    private static void BuildStopper(Node3D root, float width)
    {
        root.AddChild(BoxMesh(
            GateNode,
            new Vector3(0.1f, 0.3f, width),
            Vector3.Zero,
            Accent));

        // Berço: parado, sempre abaixo da trava recolhida.
        root.AddChild(BoxMesh(
            "cradle",
            new Vector3(0.16f, 0.1f, width + 0.06f),
            new Vector3(0f, -0.55f, 0f),
            Frame));
    }

    /// <summary>
    /// Sensor: caixa preta com a lente virada para a direção de leitura
    /// (+X nos de barreira e proximidade, −Y no de altura, exatamente os eixos
    /// que PhotoSensor/HeightSensor usam no raycast). A lente é o nó que
    /// SceneView acende quando Detected.
    /// </summary>
    private static void BuildSensor(Node3D root, Vector3 facing)
    {
        const float housing = 0.09f;

        root.AddChild(BoxMesh(
            "body",
            new Vector3(housing, housing, housing * 0.8f),
            -facing * 0.01f,
            Housing));

        // Suporte de fixação atrás do corpo, no lado oposto à leitura.
        root.AddChild(BoxMesh(
            "bracket",
            new Vector3(0.03f, 0.03f, 0.06f),
            -facing * (housing / 2f + 0.015f),
            Frame));

        var lens = Cylinder(
            LensNode,
            0.02f,
            0.028f,
            facing * (housing / 2f + 0.005f),
            facing == Vector3.Down ? Axis.Y : Axis.X,
            LensMaterial());
        root.AddChild(lens);
    }

    /// <summary>Sinaleiro: base de metal e domo translúcido que SceneView
    /// acende lendo IndicatorLight.On.</summary>
    private static void BuildIndicatorLight(Node3D root)
    {
        root.AddChild(Cylinder("base", 0.03f, 0.055f, new Vector3(0f, -0.045f, 0f), Axis.Y, Frame));

        root.AddChild(new MeshInstance3D
        {
            Name = LampNode,
            Mesh = new SphereMesh
            {
                Radius = 0.05f,
                Height = 0.09f,
                RadialSegments = 16,
                Rings = 8,
                Material = LensMaterial(),
            },
            Position = new Vector3(0f, 0.005f, 0f),
        });
    }

    /// <summary>Botoeira: placa de fundo e cabeça de cogumelo.</summary>
    private static void BuildPushButton(Node3D root)
    {
        root.AddChild(Cylinder("plate", 0.02f, 0.055f, new Vector3(0f, -0.02f, 0f), Axis.Y, Frame));
        root.AddChild(Cylinder("cap", 0.028f, 0.04f, new Vector3(0f, 0.005f, 0f), Axis.Y, Button));
    }

    /// <summary>Chave seletora: mesma placa da botoeira, com alavanca.</summary>
    private static void BuildSelectorSwitch(Node3D root)
    {
        root.AddChild(Cylinder("plate", 0.02f, 0.055f, new Vector3(0f, -0.02f, 0f), Axis.Y, Frame));
        root.AddChild(Cylinder("collar", 0.02f, 0.03f, new Vector3(0f, 0f, 0f), Axis.Y, Steel));
        root.AddChild(BoxMesh("lever", new Vector3(0.07f, 0.016f, 0.02f),
            new Vector3(0f, 0.018f, 0f), Steel));
    }

    /// <summary>Alimentador: funil sobre um bocal — indica de onde as peças
    /// caem sem esconder a peça recém-criada.</summary>
    private static void BuildEmitter(Node3D root)
    {
        root.AddChild(new MeshInstance3D
        {
            Name = "hopper",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.16f,
                BottomRadius = 0.06f,
                Height = 0.2f,
                RadialSegments = 16,
                Rings = 0,
                Material = Frame,
            },
            Position = new Vector3(0f, 0.05f, 0f),
        });

        root.AddChild(Cylinder("spout", 0.08f, 0.06f, new Vector3(0f, -0.09f, 0f), Axis.Y, Accent));
    }

    /// <summary>Saída: caçamba translúcida (o volume que apaga a peça é
    /// informação, não decoração) com aro sólido para dar borda.</summary>
    private static void BuildSink(Node3D root, Vector3 size, Color color, float alpha)
    {
        root.AddChild(BoxMesh("body", size, Vector3.Zero, Flat(color, alpha)));

        // Aro superior: quatro barras finas dando contorno à boca da caçamba.
        float top = size.Y / 2f;
        const float bar = 0.03f;
        root.AddChild(BoxMesh("rim-x+", new Vector3(size.X, bar, bar),
            new Vector3(0f, top, size.Z / 2f), Frame));
        root.AddChild(BoxMesh("rim-x-", new Vector3(size.X, bar, bar),
            new Vector3(0f, top, -size.Z / 2f), Frame));
        root.AddChild(BoxMesh("rim-z+", new Vector3(bar, bar, size.Z),
            new Vector3(size.X / 2f, top, 0f), Frame));
        root.AddChild(BoxMesh("rim-z-", new Vector3(bar, bar, size.Z),
            new Vector3(-size.X / 2f, top, 0f), Frame));
    }

    /// <summary>
    /// Corpo estático. Todos compartilham o mesmo comportamento na física, e
    /// é só aqui — na camada que desenha — que vale distinguir pelo tipo:
    /// a rampa ganha guias laterais porque sem elas ela lê como uma tábua.
    /// </summary>
    private static void BuildStaticBody(Node3D root, string typeId, Vector3 size, Color color)
    {
        var material = Metal(color, 0.05f, 0.85f);
        root.AddChild(BoxMesh("body", size, Vector3.Zero, material));

        if (typeId != "chute")
            return;

        float railHeight = Mathf.Max(size.Y * 1.5f, 0.08f);
        var railSize = new Vector3(size.X, railHeight, 0.03f);
        float railY = size.Y / 2f + railHeight / 2f;
        foreach (int sz in new[] { -1, 1 })
        {
            root.AddChild(BoxMesh(
                $"rail-{sz}",
                railSize,
                new Vector3(0f, railY, sz * (size.Z / 2f - 0.015f)),
                Frame));
        }
    }

    private static MeshInstance3D BoxMesh(string name, Vector3 size, Vector3 pos, Material material) =>
        new()
        {
            Name = name,
            Mesh = new BoxMesh { Size = size, Material = material },
            Position = pos,
        };

    /// <summary>Cilindro deitado no eixo Z (roletes e tambores).</summary>
    private static MeshInstance3D Roller(string name, float length, float radius, Vector3 pos, Material material) =>
        Cylinder(name, length, radius, pos, Axis.Z, material);

    private enum Axis { X, Y, Z }

    /// <summary>Cilindro com o comprimento ao longo do eixo pedido (CylinderMesh
    /// nasce em pé, no Y).</summary>
    private static MeshInstance3D Cylinder(
        string name, float length, float radius, Vector3 pos, Axis axis, Material material) =>
        new()
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = length,
                RadialSegments = 12,
                Rings = 0,
                Material = material,
            },
            Position = pos,
            RotationDegrees = axis switch
            {
                Axis.X => new Vector3(0f, 0f, 90f),
                Axis.Z => new Vector3(90f, 0f, 0f),
                _ => Vector3.Zero,
            },
        };

    /// <summary>
    /// Material de lente/lâmpada. Tem de ser NOVO a cada chamada: SceneView
    /// liga a emissão instância por instância, e um material de paleta
    /// compartilhado acenderia todos os sensores da cena de uma vez.
    /// </summary>
    private static StandardMaterial3D LensMaterial() => new()
    {
        AlbedoColor = new Color(0.30f, 0.30f, 0.20f),
        Metallic = 0.1f,
        Roughness = 0.25f,
    };

    private static StandardMaterial3D Metal(Color color, float metallic, float roughness) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = roughness,
    };

    private static StandardMaterial3D Flat(Color color, float alpha)
    {
        color.A = alpha;
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.7f,
            Transparency = alpha < 1f
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
        };
    }

    /// <summary>Parâmetro da instância com fallback no default do catálogo —
    /// mesma resolução do DeviceBehavior, replicada porque a camada 4 não
    /// enxerga behaviors instanciados em Edit.</summary>
    internal static float GetFloat(DeviceInstance instance, DeviceTypeDef type, string name, float fallback)
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
