using Forja.Studio.Editor;
using Godot;

namespace Forja.Studio.Rendering;

/// <summary>
/// Grade do plano y=0 no modo Edit.
///
/// Por que existe: o CatalogPanel manda "clique no chão para colocar" e o
/// SelectionManager de fato projeta o clique no plano y=0 — mas não havia
/// chão NENHUM desenhado. A cena vazia era um vazio uniforme, sem horizonte
/// e sem referência de escala, então não dava para saber onde é o zero, para
/// que lado cresce o X, nem quanto é um metro.
///
/// É chrome do editor, não conteúdo de cena: fica oculta em Run de propósito.
/// Desenhar um piso que a física não tem faria as peças parecerem atravessar
/// o chão — o que é mentira sobre o que está sendo simulado.
/// </summary>
public partial class EditorGrid : Node3D
{
    private const int HalfSpan = 12;     // metros para cada lado do zero
    private const int MajorEvery = 5;    // linha reforçada a cada 5 m

    private static readonly Color MinorColor = new(1f, 1f, 1f, 0.07f);
    private static readonly Color MajorColor = new(1f, 1f, 1f, 0.16f);
    private static readonly Color AxisXColor = new(0.85f, 0.35f, 0.32f, 0.55f);
    private static readonly Color AxisZColor = new(0.36f, 0.55f, 0.85f, 0.55f);

    private readonly EditorContext _ctx;

    public EditorGrid(EditorContext ctx) => _ctx = ctx;

    public override void _Ready()
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            // A grade é referência, não geometria: não deve receber sombra
            // nem tampar o que está atrás dela.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        for (int i = -HalfSpan; i <= HalfSpan; i++)
        {
            Color color = i == 0
                ? AxisZColor
                : i % MajorEvery == 0 ? MajorColor : MinorColor;
            Line(mesh, new Vector3(i, 0f, -HalfSpan), new Vector3(i, 0f, HalfSpan), color);

            color = i == 0
                ? AxisXColor
                : i % MajorEvery == 0 ? MajorColor : MinorColor;
            Line(mesh, new Vector3(-HalfSpan, 0f, i), new Vector3(HalfSpan, 0f, i), color);
        }
        mesh.SurfaceEnd();

        AddChild(new MeshInstance3D
        {
            Name = "grid",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public override void _Process(double delta) => Visible = _ctx.InEdit;

    private static void Line(ImmediateMesh mesh, Vector3 a, Vector3 b, Color color)
    {
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(b);
    }
}
