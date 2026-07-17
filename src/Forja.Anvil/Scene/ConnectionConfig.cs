namespace Forja.Anvil.Scene;

/// <summary>
/// Configuração de conexão com o PLC (data-model §5). Trocar de driver é
/// configuração, nunca recompilação (Artigo IV.2).
/// </summary>
public sealed record ConnectionConfig
{
    public const string NullDriverKey = "null";
    public const string ModbusTcpServerKey = "modbus-tcp-server";
    public const string ModbusTcpClientKey = "modbus-tcp-client";

    /// <summary>Chave do driver: "null", "modbus-tcp-server" ou "modbus-tcp-client".</summary>
    public string Driver { get; init; } = NullDriverKey;

    /// <summary>Endereço de escuta no modo servidor.</summary>
    public string BindAddress { get; init; } = "0.0.0.0";

    /// <summary>IP do PLC no modo cliente.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>Servidor: porta de escuta. Cliente: porta do PLC.</summary>
    public ushort Port { get; init; } = 502;

    /// <summary>Unit/slave id Modbus.</summary>
    public byte UnitId { get; init; } = 1;

    /// <summary>Timeout de I/O em ms — sempre explícito (Artigo VII.2).</summary>
    public int TimeoutMs { get; init; } = 1000;
}
