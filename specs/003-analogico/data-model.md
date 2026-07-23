# Data Model — Fase 2: sinais analógicos

Fase 1 do [plan.md](plan.md). Entidades novas e campos acrescentados, com regras
de validação e efeito no determinismo. Tipos concretos em C# (`Forja.Anvil`);
onde um arquivo já existe, a mudança está marcada como **+= aditiva**.

## 1. Palavra de I/O (transitória, no contrato)

Valor bruto de 16 bits que trafega no `IoSnapshot`, indexado por offset dentro
da sua área. Não é persistida; nasce e morre no tick.

```csharp
// IPlcDriver.cs — += Words
public readonly record struct IoSnapshot(
    ulong TickNumber,
    ReadOnlyMemory<bool>   Bits,
    ReadOnlyMemory<ushort> Words,
    bool Valid = true)
{
    public static IoSnapshot Empty(ulong tick) =>
        new(tick, ReadOnlyMemory<bool>.Empty, ReadOnlyMemory<ushort>.Empty);
}
```

- **Entrada**: `Bits` = discrete inputs, `Words` = input registers.
- **Saída**: `Bits` = coils, `Words` = holding registers.
- **Invariante**: sempre `ushort` bruto. `float` (EU) nunca aparece aqui.
- **Determinismo**: entra no hash em ordem de endereço (§6).

## 2. PortType — tipo de dado da porta (catálogo)

```csharp
public enum PortType { Bool, Word }

// DeviceTypeDef.cs — += Type com default
public sealed record PortDef(string PortName, IoDirection Direction, PortType Type = PortType.Bool);
```

- **Default `Bool`**: os 18 dispositivos e todo catálogo JSON existente seguem
  válidos sem edição (SC-007).
- **`Word`**: declarado pelos dispositivos analógicos novos.
- **Validação**: a matriz direção×área×tipo (contrato `scaling-eu-raw.md` / R7).

## 3. Faixa de engenharia (parâmetro do tipo)

Reusa `ParamDef` — **nenhum tipo novo**. Cada dispositivo analógico declara
`euMin`/`euMax` como parâmetros numéricos no seu catálogo JSON.

```jsonc
// catalog/devices/level-sensor.json (trecho)
"paramDefs": [
  { "name": "euMin", "type": "float", "default": 0,   "min": -1000, "max": 1000 },
  { "name": "euMax", "type": "float", "default": 100, "min": -1000, "max": 1000 }
]
```

- Lido no comportamento via `GetFloat("euMin")` / `GetFloat("euMax")`
  (`DeviceBehavior.cs:57`).
- **Vive só na conversão** — não trafega no fio.

## 4. AnalogScale — a faixa bruta do cartão (cena, por instância)

```csharp
// IoTypes.cs — novo record
public sealed record AnalogScale(ushort RawMin = 0, ushort RawMax = 65535);

// IoTag += Scale opcional
public sealed record IoTag(uint DeviceId, string PortName, IoAddress Address, AnalogScale? Scale = null);
```

- **Presente** só quando a porta mapeada é `Word`; `null` em tags de bit.
- **Por instância**: dois sensores iguais podem ter cartões diferentes (AS4/US1).
- **Aditivo**: cena v1 sem `scale` desserializa com `null` (R3).
- **Validação `invalid-scale`**: `RawMin == RawMax` é erro (evita divisão por
  zero na conversão).

## 5. Dispositivos analógicos (comportamento + catálogo)

| Dispositivo | Categoria | Porta | PortType | Direção | EU (exemplo) |
|---|---|---|---|---|---|
| Sensor de nível/distância | Sensor | `level` | Word | In | 0–100 cm |
| Balança | Sensor | `weight` | Word | In | 0–50 kg |
| Esteira de velocidade variável | Transport | `speed` | Word | Out | 0–2 m/s |

Cada um: `class : DeviceBehavior` em `Forja.Core/Devices/`, JSON em
`catalog/devices/`, e ao menos um cenário headless (Artigo V, FR-018).

- Sensor: `Tick` calcula a grandeza física e chama `ctx.Io.SetInputWord(Id, "level", eu)`.
- Atuador: `Tick` chama `float sp = ctx.Io.GetOutputWord(Id, "speed")` e aplica.
- `WriteState`: hasheia o estado interno relevante (a palavra já entra pelo hash
  da IoTable; o device hasheia o que for seu além disso).

## 6. IoTable — buffers, conversão, força e hash (camada 2)

```csharp
// IoTable.cs — += canais de palavra paralelos aos de bit
private readonly ushort[]  _inputWords;
private readonly ushort[]  _outputWords;
private readonly ushort?[] _forcedInputWords;
private readonly ushort?[] _forcedOutputWords;
```

**Escrita/leitura (fronteira EU↔bruto)**:

```csharp
public void  SetInputWord(uint id, string port, float eu);   // EU → bruto, satura
public float GetOutputWord(uint id, string port);            // bruto → EU
public void  ForceWord(IoAddress addr, ushort? raw);         // força bruto; null libera
```

**Snapshot**: `BuildInputSnapshot` passa a preencher `Words`;
`ApplyOutputSnapshot` copia `snapshot.Words.Span` para `_outputWords`.

**Hash (Artigo I.4)** — palavras junto dos bits, ordem de endereço:

```csharp
public void WriteState(ref StateHasher hasher)
{
    foreach (bool b   in _inputs)      hasher.Add(b);
    foreach (bool b   in _outputs)     hasher.Add(b);
    foreach (ushort w in _inputWords)  hasher.Add(w);   // += palavras
    foreach (ushort w in _outputWords) hasher.Add(w);
}
```

**IoPointView** += valor numérico + unidade para a tabela de I/O:

```csharp
public sealed record IoPointView(
    uint DeviceId, string PortName, IoAddress Address, IoDirection Direction,
    bool Value, bool Forced,
    bool IsAnalog = false, float AnalogEu = 0f, ushort AnalogRaw = 0, string Unit = "");
```

## 7. Schema v2 — migração aditiva

```csharp
// SceneDocument.cs
public const int CurrentSchemaVersion = 2;   // era 1

// SceneSerializer.cs — registrar na cadeia Migrations
internal sealed class MigrationV1ToV2 : ISceneMigration
{
    public int FromVersion => 1;
    public JsonNode Apply(JsonNode scene) => scene; // aditiva: só o carimbo de versão sobe (feito pelo Load)
}
```

- **Obrigatória** (R4): sem ela, `Load` falha em toda cena v1 com
  "sem migração registrada de 1 → 2".
- **Não-destrutiva**: não remove nem renomeia campo; os campos novos
  (`scale`, `PortType`) entram por default do modelo na desserialização.
- **Falha explícita** (FR-010): campo v2 desconhecido → `UnmappedMemberHandling.Disallow`
  já rejeita com caminho + motivo.

## 8. Regras de validação (IoMapValidator)

| Código | Regra | Origem |
|---|---|---|
| ~~`analog-not-supported`~~ | **removida** | R7 |
| `type-area-mismatch` | célula fora da matriz direção×área×tipo (§ contrato) | FR-011, FR-012 |
| `invalid-scale` | `RawMin == RawMax` (ou EU degenerada no tipo) | edge case |
| `duplicate-address` | já cobre palavra (o `IoAddress` inclui a área) | FR-013 |
| `missing-tag`, `duplicate-port-tag`, `orphan-tag` | inalteradas | — |

## Rastreamento

Cada entidade mapeia a requisitos da [spec.md](spec.md): Palavra→FR-001..003;
PortType→FR-008; EU→FR-005; AnalogScale→FR-004,FR-006,FR-007; IoTable
hash→FR-014; buffers→FR-015; schema→FR-009,FR-010; validação→FR-011..013;
dispositivos→FR-016..018; IoPointView→FR-019,FR-020.
