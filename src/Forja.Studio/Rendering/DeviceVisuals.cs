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
/// e colisão do editor — o desenho vive dentro dela.
/// </summary>
public static class DeviceVisuals
{
    /// <summary>Nome do nó da lâmpada; SceneView acende lendo o core.</summary>
    public const string LampNode = "lamp";

    /// <summary>Nome do nó que se move com o atuador (haste do pistão).</summary>
    public const string RodNode = "rod";

    // Paleta industrial: estrutura em cinza claro, superfícies de trabalho em
    // borracha escura, metal só onde há peça girando ou deslizando.
    private static readonly Color FrameColor = new(0.62f, 0.64f, 0.67f);
    private static readonly Color RubberColor = new(0.16f, 0.17f, 0.19f);
    private static readonly Color SteelColor = new(0.72f, 0.74f, 0.78f);
    private static readonly Color AccentColor = new(0.85f, 0.65f, 0.15f);

    private static StandardMaterial3D? _frame;
    private static StandardMaterial3D? _rubber;
    private static StandardMaterial3D? _steel;
    private static StandardMaterial3D? _accent;

    private static StandardMaterial3D Frame => _frame ??= Metal(FrameColor, 0.35f, 0.55f);
    private static StandardMaterial3D Rubber => _rubber ??= Metal(RubberColor, 0.0f, 0.85f);
    private static StandardMaterial3D Steel => _steel ??= Metal(SteelColor, 0.85f, 0.25f);
    private static StandardMaterial3D Accent => _accent ??= Metal(AccentColor, 0.2f, 0.5f);

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

    private static MeshInstance3D BoxMesh(string name, Vector3 size, Vector3 pos, Material material) =>
        new()
        {
            Name = name,
            Mesh = new BoxMesh { Size = size, Material = material },
            Position = pos,
        };

    /// <summary>Cilindro deitado no eixo Z (roletes e tambores).</summary>
    private static MeshInstance3D Roller(string name, float length, float radius, Vector3 pos, Material material) =>
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
            RotationDegrees = new Vector3(90f, 0f, 0f),
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
}
