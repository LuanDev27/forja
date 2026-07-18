using Godot;

namespace Forja.Studio.Editor;

/// <summary>
/// Indicador de movimento (T038): cruz de eixos X/Z no dispositivo ativo. O
/// arrasto em si acontece no plano do chão (SelectionManager) com snap de
/// 0,1 m — a cruz mostra onde o objeto está e que ele é arrastável.
/// </summary>
public partial class MoveGizmo : Node3D
{
    public override void _Ready()
    {
        AddAxis(new Vector3(0.9f, 0.03f, 0.03f), new Color(0.78f, 0.35f, 0.30f));
        AddAxis(new Vector3(0.03f, 0.03f, 0.9f), new Color(0.30f, 0.45f, 0.78f));
        AddAxis(new Vector3(0.08f, 0.08f, 0.08f), new Color(1f, 0.85f, 0.2f));
        Visible = false;
    }

    private void AddAxis(Vector3 size, Color color)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh(),
            Scale = size,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
            },
        });
    }
}
