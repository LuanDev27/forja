using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;

namespace Forja.Anvil.Validation;

/// <summary>Erro de validação com os dispositivos envolvidos (RF-05).</summary>
public sealed record ValidationError(string Code, string Message, IReadOnlyList<uint> DeviceIds);

/// <summary>
/// Regras V1–V3 do data-model §4. Erros aqui BLOQUEIAM a transição Edit→Run
/// (Artigo VI.3 — conflito de endereço é erro, não warning).
/// </summary>
public static class IoMapValidator
{
    public static List<ValidationError> Validate(SceneDocument doc, DeviceCatalog catalog)
    {
        var errors = new List<ValidationError>();
        bool clientMode = doc.Connection.Driver == ConnectionConfig.ModbusTcpClientKey;

        var devicesById = doc.Devices.ToDictionary(d => d.Id);
        var byAddress = new Dictionary<IoAddress, IoTag>();
        var taggedPorts = new HashSet<(uint DeviceId, string Port)>();

        foreach (var tag in doc.IoMap)
        {
            // Tag órfã (V3)
            if (!devicesById.TryGetValue(tag.DeviceId, out var device))
            {
                errors.Add(new ValidationError(
                    "orphan-tag",
                    $"Tag '{tag.PortName}' referencia dispositivo inexistente (id {tag.DeviceId}).",
                    new[] { tag.DeviceId }));
                continue;
            }

            if (!catalog.TryGet(device.TypeId, out var type))
            {
                errors.Add(new ValidationError(
                    "unknown-type",
                    $"Dispositivo {device.Id} tem tipo desconhecido '{device.TypeId}'.",
                    new[] { device.Id }));
                continue;
            }

            var port = type.Ports.FirstOrDefault(p => p.PortName == tag.PortName);
            if (port is null)
            {
                errors.Add(new ValidationError(
                    "unknown-port",
                    $"Dispositivo {device.Id} ('{type.DisplayName}') não tem porta '{tag.PortName}'.",
                    new[] { device.Id }));
                continue;
            }

            // Uma tag por porta (Artigo VI.1)
            if (!taggedPorts.Add((tag.DeviceId, tag.PortName)))
            {
                errors.Add(new ValidationError(
                    "duplicate-port-tag",
                    $"Porta '{tag.PortName}' do dispositivo {device.Id} tem mais de uma tag.",
                    new[] { device.Id }));
            }

            // Matriz direção × área × tipo (Fase 2, contrato scaling-eu-raw V1).
            // Modo cliente: a Forja é master e pode ler/escrever as áreas do
            // master remoto (limite do protocolo — contracts/modbus-mapping.md).
            bool areaOk = (port.Type, port.Direction) switch
            {
                (PortType.Bool, IoDirection.In) => tag.Address.Area == IoArea.DiscreteInput
                                  || (clientMode && tag.Address.Area == IoArea.Coil),
                (PortType.Bool, IoDirection.Out) => tag.Address.Area == IoArea.Coil,
                (PortType.Word, IoDirection.In) => tag.Address.Area == IoArea.InputRegister
                                  || (clientMode && tag.Address.Area == IoArea.HoldingRegister),
                (PortType.Word, IoDirection.Out) => tag.Address.Area == IoArea.HoldingRegister,
                _ => false,
            };
            if (!areaOk)
            {
                errors.Add(new ValidationError(
                    "type-area-mismatch",
                    $"Porta '{tag.PortName}' ({port.Direction}, {port.Type}) do dispositivo {device.Id} " +
                    $"não pode mapear em {tag.Address.ToDisplay()}.",
                    new[] { device.Id }));
            }

            // Escala do cartão degenerada (V2): faixa bruta nula → divisão por
            // zero na conversão. Só se aplica a portas de palavra.
            if (port.Type == PortType.Word && tag.Scale is { } scale && scale.RawMin == scale.RawMax)
            {
                errors.Add(new ValidationError(
                    "invalid-scale",
                    $"Porta '{tag.PortName}' do dispositivo {device.Id} tem faixa bruta nula " +
                    $"(rawMin == rawMax == {scale.RawMin}).",
                    new[] { device.Id }));
            }

            // Endereço duplicado (V1) — cita OS DOIS dispositivos (RF-05).
            if (byAddress.TryGetValue(tag.Address, out var other))
            {
                errors.Add(new ValidationError(
                    "duplicate-address",
                    $"Endereço {tag.Address.ToDisplay()} usado por dois dispositivos: " +
                    $"{other.DeviceId} (porta '{other.PortName}') e {tag.DeviceId} (porta '{tag.PortName}').",
                    new[] { other.DeviceId, tag.DeviceId }));
            }
            else
            {
                byAddress[tag.Address] = tag;
            }
        }

        // Toda porta declarada precisa de tag (V3).
        foreach (var device in doc.Devices)
        {
            if (!catalog.TryGet(device.TypeId, out var type))
            {
                errors.Add(new ValidationError(
                    "unknown-type",
                    $"Dispositivo {device.Id} tem tipo desconhecido '{device.TypeId}'.",
                    new[] { device.Id }));
                continue;
            }

            foreach (var port in type.Ports)
            {
                if (!taggedPorts.Contains((device.Id, port.PortName)))
                {
                    errors.Add(new ValidationError(
                        "missing-tag",
                        $"Porta '{port.PortName}' do dispositivo {device.Id} ('{type.DisplayName}') " +
                        "não tem endereço mapeado.",
                        new[] { device.Id }));
                }
            }
        }

        return errors;
    }
}
