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
/// Snapshot de I/O trocado com o driver, indexado por offset (0..N-1, N = maior
/// offset usado + 1). <see cref="Bits"/> carrega os digitais (discrete inputs na
/// entrada, coils na saída) e <see cref="Words"/> os analógicos brutos de 16 bits
/// (input registers na entrada, holding registers na saída) — canais paralelos,
/// uma troca só (ADR 0005, decisão 1; contrato W1/W2). Valid=false marca snapshot
/// obsoleto devolvido durante falha (regra C1). Palavra é sempre bruto: a unidade
/// de engenharia vive só na fronteira IoTable, nunca no fio (contrato W3).
/// </summary>
public readonly record struct IoSnapshot(
    ulong TickNumber,
    ReadOnlyMemory<bool> Bits,
    ReadOnlyMemory<ushort> Words = default,
    bool Valid = true)
{
    public static IoSnapshot Empty(ulong tick) =>
        new(tick, ReadOnlyMemory<bool>.Empty, ReadOnlyMemory<ushort>.Empty);
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
