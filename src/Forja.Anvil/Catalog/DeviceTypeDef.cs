using System.Text.Json;
using System.Text.Json.Serialization;
using Forja.Anvil.Scene;

namespace Forja.Anvil.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<DeviceCategory>))]
public enum DeviceCategory
{
    Passive,
    Transport,
    Sensor,
    Actuator,
    SourceSink,
    Part,
    Hmi,
}

/// <summary>Porta de I/O declarada pelo tipo. Cada porta mapeia para exatamente
/// um endereço (Artigo VI.1).</summary>
public sealed record PortDef(string PortName, IoDirection Direction);

/// <summary>Definição de parâmetro editável do dispositivo.</summary>
public sealed record ParamDef
{
    public required string Name { get; init; }

    /// <summary>"bool" | "int" | "float" | "enum" | "string".</summary>
    public required string Type { get; init; }

    public JsonElement? Default { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>Valores possíveis quando Type == "enum".</summary>
    public List<string>? Values { get; init; }
}

/// <summary>
/// Entrada do catálogo data-driven (Artigo III.2): adicionar um tipo novo é
/// adicionar um JSON em catalog/devices/ — o editor não recompila.
/// </summary>
public sealed record DeviceTypeDef
{
    public required string TypeId { get; init; }
    public required DeviceCategory Category { get; init; }
    public required string DisplayName { get; init; }

    public List<PortDef> Ports { get; init; } = new();
    public List<ParamDef> ParamDefs { get; init; } = new();

    /// <summary>Cena visual (.tscn) usada só pela camada 4.</summary>
    public string VisualScene { get; init; } = "";

    /// <summary>Colocação no editor: true = assenta com o TOPO no nível do
    /// chão (pisos/lajes — não engolem o que está sobre elas); false =
    /// assenta com a base no chão.</summary>
    public bool FlushToGround { get; init; }

    /// <summary>Chave do comportamento registrado na DeviceFactory do core.</summary>
    public required string Behavior { get; init; }
}
