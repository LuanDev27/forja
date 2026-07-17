using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Bellows.Modbus;
using Forja.Bellows.Null;

namespace Forja.Bellows;

/// <summary>
/// Resolve ConnectionConfig.Driver → implementação (Artigo IV.2: trocar de
/// driver é configuração). Chave desconhecida falha alto (Artigo VII.3).
/// </summary>
public static class DriverRegistry
{
    /// <param name="outputCount">Bits de saída da cena (IoTable.OutputCount);
    /// dimensiona os buffers e a leitura FC01 no modo cliente.</param>
    public static IPlcDriver Create(ConnectionConfig config, int outputCount) =>
        config.Driver switch
        {
            ConnectionConfig.NullDriverKey => new NullDriver(Math.Max(outputCount, 1)),
            ConnectionConfig.ModbusTcpServerKey => new ModbusTcpServerDriver(Math.Max(outputCount, 1)),
            ConnectionConfig.ModbusTcpClientKey => new ModbusTcpClientDriver(Math.Max(outputCount, 1)),
            _ => throw new ArgumentException(
                $"Driver desconhecido '{config.Driver}'. Válidos: " +
                $"'{ConnectionConfig.NullDriverKey}', '{ConnectionConfig.ModbusTcpServerKey}', " +
                $"'{ConnectionConfig.ModbusTcpClientKey}'.", nameof(config)),
        };
}
