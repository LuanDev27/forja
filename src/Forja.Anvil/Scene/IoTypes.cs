using System.Text.Json.Serialization;

namespace Forja.Anvil.Scene;

/// <summary>Áreas Modbus. v1 é digital-only: registers são rejeitados na validação.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IoArea>))]
public enum IoArea
{
    DiscreteInput,
    Coil,
    InputRegister,
    HoldingRegister,
}

/// <summary>Direção do ponto de vista da Forja: In = sensor→PLC, Out = PLC→atuador.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IoDirection>))]
public enum IoDirection
{
    In,
    Out,
}

/// <summary>Endereço canônico (decisão Q2: cru no arquivo; IEC só na UI).</summary>
public readonly record struct IoAddress(IoArea Area, ushort Offset)
{
    /// <summary>Notação IEC derivada para exibição: %IX0.3 / %QX1.0.</summary>
    public string ToIec() => Area switch
    {
        IoArea.DiscreteInput => $"%IX{Offset / 8}.{Offset % 8}",
        IoArea.Coil => $"%QX{Offset / 8}.{Offset % 8}",
        IoArea.InputRegister => $"%IW{Offset}",
        IoArea.HoldingRegister => $"%QW{Offset}",
        _ => Offset.ToString(),
    };

    /// <summary>Rótulo duplo da UI, ex.: "%IX0.3 (DI 3)".</summary>
    public string ToDisplay() => Area switch
    {
        IoArea.DiscreteInput => $"{ToIec()} (DI {Offset})",
        IoArea.Coil => $"{ToIec()} (Coil {Offset})",
        _ => ToIec(),
    };
}

/// <summary>
/// Mapeia device.port → endereço. Artigo VI: contrato explícito, salvo na
/// cena, sem mapeamento implícito.
/// </summary>
public sealed record IoTag(uint DeviceId, string PortName, IoAddress Address);
