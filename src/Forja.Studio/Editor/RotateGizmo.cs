using Godot;

namespace Forja.Studio.Editor;

/// <summary>
/// Anel de rotação (T038) em torno de Y no dispositivo ativo. Arrastar sobre o
/// anel gira com snap de 15° (SelectionManager comita RotateDeviceCommand ao
/// soltar). Também fornece a matemática raio→ângulo no plano do anel.
/// </summary>
public partial class RotateGizmo : Node3D
{
    public const float Radius = 0.9f;
    public const float Band = 0.15f;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = Radius - 0.05f, OuterRadius = Radius + 0.05f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.55f, 0.70f, 0.45f, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
            },
        });
        Visible = false;
    }

    /// <summary>Clique inicia rotação só se acertar a faixa do anel.</summary>
    public bool HitsRing(Vector3 rayOrigin, Vector3 rayDir, Vector3 center, out float angleDeg) =>
        TryAngle(rayOrigin, rayDir, center, requireRing: true, out angleDeg);

    /// <summary>Ângulo do cursor no plano do anel (sem restrição de raio —
    /// usado durante o arrasto). Convenção igual à Pose.RotY: positivo gira
    /// +X em direção a −Z (Basis.FromEuler).</summary>
    public bool TryAngle(Vector3 rayOrigin, Vector3 rayDir, Vector3 center, bool requireRing, out float angleDeg)
    {
        angleDeg = 0f;
        if (new Plane(Vector3.Up, center.Y).IntersectsRay(rayOrigin, rayDir) is not { } hit)
            return false;

        var radial = hit - center;
        radial.Y = 0f;
        float len = radial.Length();
        if (len < 0.01f)
            return false;
        if (requireRing && (len < Radius - Band || len > Radius + Band))
            return false;

        angleDeg = Mathf.RadToDeg(Mathf.Atan2(-radial.Z, radial.X));
        return true;
    }
}
