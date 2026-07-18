using Godot;

namespace Forja.Studio.Editor;

/// <summary>
/// Câmera orbital do editor (T038). Botão do meio orbita, direito faz pan,
/// roda dá zoom. Fica sempre ativa — também serve para observar a simulação
/// rodando. Não toca no estado do core (Artigo II.2).
/// </summary>
public partial class EditorCamera : Camera3D
{
    private float _yaw = 0.7f;
    private float _pitch = 0.6f;
    private float _distance = 7f;
    private Vector3 _focus = new(0f, 0.3f, 0f);
    private bool _orbiting;
    private bool _panning;

    public override void _Ready()
    {
        Current = true;
        Apply();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb:
                HandleButton(mb);
                break;
            case InputEventMouseMotion mm when _orbiting:
                _yaw -= mm.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.01f, -1.45f, 1.45f);
                Apply();
                break;
            case InputEventMouseMotion mm when _panning:
                var basis = GlobalTransform.Basis;
                _focus += (-basis.X * mm.Relative.X + basis.Y * mm.Relative.Y) * (_distance * 0.0015f);
                Apply();
                break;
        }
    }

    private void HandleButton(InputEventMouseButton mb)
    {
        switch (mb.ButtonIndex)
        {
            case MouseButton.Middle:
                _orbiting = mb.Pressed;
                break;
            case MouseButton.Right:
                _panning = mb.Pressed;
                break;
            case MouseButton.WheelUp when mb.Pressed:
                _distance = Mathf.Max(1.5f, _distance * 0.9f);
                Apply();
                break;
            case MouseButton.WheelDown when mb.Pressed:
                _distance = Mathf.Min(40f, _distance * 1.1f);
                Apply();
                break;
        }
    }

    private void Apply()
    {
        var dir = new Vector3(
            Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
            Mathf.Sin(_pitch),
            Mathf.Cos(_pitch) * Mathf.Cos(_yaw));
        LookAtFromPosition(_focus + dir * _distance, _focus, Vector3.Up);
    }
}
