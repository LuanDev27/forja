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

/// <summary>
/// Tipo de dado da porta (Fase 2, ADR 0005). Bool = um bit (discrete input/coil);
/// Word = uma palavra de 16 bits (input/holding register). Default Bool: os
/// dispositivos digitais seguem sem tocar em nada.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PortType>))]
public enum PortType
{
    Bool,
    Word,
}

/// <summary>
/// Faixa bruta do "cartão" analógico, por instância do ponto na cena (ADR 0005,
/// decisão 2). O registrador carrega bruto nesta faixa; a fronteira IoTable
/// converte de/para a unidade de engenharia do device. RawMin==RawMax é erro
/// de validação (evita divisão por zero na conversão).
/// </summary>
public sealed record AnalogScale(ushort RawMin = 0, ushort RawMax = 65535);

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
/// cena, sem mapeamento implícito. <see cref="Scale"/> é preenchido só quando a
/// porta é <see cref="PortType.Word"/>; null em portas de bit (aditivo: cena v1
/// desserializa com null).
/// </summary>
public sealed record IoTag(uint DeviceId, string PortName, IoAddress Address, AnalogScale? Scale = null);
