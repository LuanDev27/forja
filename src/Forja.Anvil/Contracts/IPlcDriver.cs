using Forja.Anvil.Scene;

namespace Forja.Anvil.Contracts;

/// <summary>
/// Estados do driver. UI: Stopped=Desconectado · Starting=Conectando/Aguardando
/// master · Ready=Conectado · Faulted=Erro.
/// </summary>
public enum DriverState
{
    Stopped,
    Starting,
    Ready,
    Faulted,
}

/// <summary>
/// Snapshot de bits digitais trocado com o driver, indexado por offset
/// (0..N-1, N = maior offset usado + 1). Valid=false marca snapshot obsoleto
/// devolvido durante falha (regra C1 do contrato).
/// </summary>
public readonly record struct IoSnapshot(ulong TickNumber, ReadOnlyMemory<bool> Bits, bool Valid = true)
{
    public static IoSnapshot Empty(ulong tick) => new(tick, ReadOnlyMemory<bool>.Empty);
}

/// <summary>
/// Único ponto de contato do núcleo com PLCs (Artigo IV.1). Implementações em
/// Forja.Bellows. Regras C1–C5: contracts/iplcdriver.md.
/// </summary>
public interface IPlcDriver : IDisposable
{
    DriverState State { get; }

    /// <summary>(novoEstado, motivo). Motivo obrigatório em Faulted (Artigo VII).</summary>
    event Action<DriverState, string?>? StateChanged;

    /// <summary>Inicia com a config da cena. Não bloqueia. Idempotente.</summary>
    void Start(ConnectionConfig config);

    /// <summary>Encerra ordenadamente. Idempotente.</summary>
    void Stop();

    /// <summary>
    /// Troca síncrona com o tick: publica inputs (sensores) e retorna outputs
    /// (atuadores). Nunca lança por falha de rede — sinaliza Faulted e retorna
    /// snapshot com Valid=false (C1). Deve respeitar ConnectionConfig.TimeoutMs.
    /// </summary>
    IoSnapshot Exchange(IoSnapshot inputs);
}
