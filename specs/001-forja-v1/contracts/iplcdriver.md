# Contrato — `IPlcDriver` (Forja.Anvil.Contracts)

O núcleo conhece **apenas** esta interface (Artigo IV). Implementações vivem
em `Forja.Bellows`. Trocar driver é configuração (`ConnectionConfig.driver`).

```csharp
namespace Forja.Anvil.Contracts;

public interface IPlcDriver : IDisposable
{
    /// Estado corrente. Mudanças disparam StateChanged.
    DriverState State { get; }
    event Action<DriverState, string?> StateChanged; // (novoEstado, motivo)

    /// Inicia o driver com a config da cena. Não bloqueia.
    void Start(ConnectionConfig config);

    /// Encerra ordenadamente. Idempotente.
    void Stop();

    /// Troca de I/O síncrona com o tick (Artigo VI / RNF-02):
    /// - `inputs`: snapshot dos sensores (produzido no fim do tick) → publicado ao PLC
    /// - retorna: snapshot das saídas vindas do PLC → consumido no tick seguinte
    /// DEVE completar dentro de ConnectionConfig.TimeoutMs; estouro ⇒ Faulted.
    IoSnapshot Exchange(IoSnapshot inputs);
}

public enum DriverState { Stopped, Starting, Ready, Faulted }
// UI mapeia: Stopped=Desconectado · Starting=Aguardando master/Conectando
//            Ready=Conectado · Faulted=Erro

public readonly record struct IoSnapshot(
    ulong TickNumber,
    ReadOnlyMemory<bool> Bits,      // ordenado por IoAddress.Offset
    IoArea Area);
```

## Regras de comportamento (testáveis)

| # | Regra | Artigo |
|---|---|---|
| C1 | `Exchange` nunca lança por falha de rede: sinaliza `Faulted` via `StateChanged` com motivo e retorna o último snapshot marcado inválido | VII.1, VII.3 |
| C2 | Ao entrar em `Faulted`, o core **pausa a simulação** — nunca segue com valores velhos silenciosamente | VII.1 |
| C3 | Timeout default 1000 ms, sempre explícito na config | VII.2 |
| C4 | `Start`/`Stop` idempotentes; `Dispose` ⇒ `Stop` | — |
| C5 | Driver não conhece dispositivos — só snapshots endereçados. O mapeamento device.port→endereço é do core (IoTable) | VI |

## Implementações v1

| Driver | Chave | Comportamento |
|---|---|---|
| `NullDriver` | `"null"` | `Exchange` devolve as saídas forçadas pela Tabela de I/O (RF-07). Nunca `Faulted`. Usado em todos os testes headless |
| `ModbusTcpServerDriver` | `"modbus-tcp-server"` | Servidor Modbus TCP (NModbus, research R3). Sensores → discrete inputs locais; coils locais ← escritos pelo master (PLC). `Ready` quando master conectado |
| `ModbusTcpClientDriver` | `"modbus-tcp-client"` | Cliente Modbus TCP: conecta em `host:port` do PLC. Sensores → `WriteCoils/WriteRegisters` no PLC; atuadores ← `ReadCoils/ReadHoldingRegisters`. `Ready` quando conexão estabelecida; queda ⇒ `Faulted` (C1/C2) |

## Testes de contrato (Forja.Bellows.Tests, xUnit, sem PLC real)

1. `NullDriver` round-trip: forçar coil 3 ⇒ `Exchange` devolve bit 3.
2. `ModbusTcpDriver` + master NModbus loopback: escrever DI 0 no snapshot ⇒
   master lê discrete input 0 = true em < 20 ms (RNF-02).
3. Master desconecta no meio ⇒ `StateChanged(Faulted, motivo)` e C1.
4. Dois `Start` seguidos não lançam; `Stop` sem `Start` não lança (C4).
