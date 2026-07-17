using Forja.Anvil.Scene;

namespace Forja.Anvil.Contracts;

/// <summary>Modos de operação (RF-01). Step executa 1 tick e cai em Pause.</summary>
public enum SimMode
{
    Edit,
    Run,
    Pause,
    Step,
}

/// <summary>
/// Comando emitido pela camada de apresentação e consumido pelo core no
/// início do próximo tick (Artigo II.2 — a UI nunca muda estado diretamente).
/// </summary>
public interface ISimCommand;

/// <summary>Pede transição de modo. O core aplica as guardas (data-model §8).</summary>
public sealed record SetModeCommand(SimMode Target) : ISimCommand;

/// <summary>
/// Força um bit de I/O (RF-05). Value null libera o override.
/// Prevalece sobre driver e sensores até ser liberado.
/// </summary>
public sealed record ForceIoCommand(IoAddress Address, bool? Value) : ISimCommand;

/// <summary>Interação de HMI (botão/chave) vinda da UI (RF-03 HMI).</summary>
public sealed record HmiCommand(uint DeviceId, string PortName, bool Value) : ISimCommand;
