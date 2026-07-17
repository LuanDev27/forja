using System.Collections.Generic;
using System.Text;
using Forja.Anvil.Validation;
using Godot;

namespace Forja.Studio.UI;

/// <summary>
/// Diálogo de validação (T032, RF-05 / Artigo VI.3). Escuta
/// <see cref="Core.Loop.SimulationLoop.ValidationFailed"/> — disparado quando
/// Edit→Run é BLOQUEADO — e lista os erros. Conflito de endereço aponta os
/// dois dispositivos envolvidos (a mensagem já vem pronta do IoMapValidator).
/// </summary>
public partial class ValidationDialog : CanvasLayer
{
    private readonly Main _main;
    private AcceptDialog _dialog = null!;

    public ValidationDialog(Main main) => _main = main;

    public override void _Ready()
    {
        _dialog = new AcceptDialog
        {
            Title = "I/O inválida — Run bloqueado",
            OkButtonText = "Voltar à edição",
            Unresizable = false,
            MinSize = new Vector2I(460, 220),
        };
        AddChild(_dialog);

        _main.Loop.ValidationFailed += OnValidationFailed;
    }

    private void OnValidationFailed(IReadOnlyList<ValidationError> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"A simulação não pode rodar — {errors.Count} problema(s) na Tabela de I/O:\n");
        foreach (var error in errors)
        {
            string ids = string.Join(", ", error.DeviceIds);
            sb.AppendLine($"• [{error.Code}] {error.Message}");
            sb.AppendLine($"    dispositivo(s): {ids}\n");
        }

        _dialog.DialogText = sb.ToString();
        // popup_centered ignora o MinSize se o texto for menor — passa o mínimo.
        _dialog.PopupCentered(new Vector2I(520, 320));
    }

    public override void _ExitTree()
    {
        if (_main.HasLoop)
            _main.Loop.ValidationFailed -= OnValidationFailed;
    }
}
