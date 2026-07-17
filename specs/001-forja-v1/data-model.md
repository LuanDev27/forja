# Data Model — Forja v1 (Phase 1)

Entidades do domínio (`Forja.Anvil`, salvo indicação). Tipos em notação C#
simplificada. O schema JSON correspondente está em
[contracts/forja-schema.md](./contracts/forja-schema.md).

---

## 1. SceneDocument (raiz do `.forja`)

| Campo | Tipo | Regras |
|---|---|---|
| `schemaVersion` | `int` | ≥ 1. Versão desconhecida/futura ⇒ erro de migração explícito |
| `name` | `string` | não vazio |
| `seed` | `ulong` | seed do `IRandomSource` da cena (Artigo I.2) |
| `devices` | `List<DeviceInstance>` | ordenada por `id` crescente — ordem canônica de iteração (Artigo I.3) |
| `ioMap` | `List<IoTag>` | sem endereços duplicados (validação bloqueante — Artigo VI.3) |
| `connection` | `ConnectionConfig` | — |

Relações: `IoTag.deviceId` → `DeviceInstance.id` (obrigatório existir);
`DeviceInstance.typeId` → `DeviceTypeDef.typeId` do catálogo.

## 2. DeviceInstance

| Campo | Tipo | Regras |
|---|---|---|
| `id` | `uint` (EntityId) | único na cena, atribuído sequencialmente, nunca reutilizado |
| `typeId` | `string` | deve existir no catálogo carregado; senão erro de carga com nome do tipo |
| `transform` | `Pose { pos: Vec3, rotY: float }` | pos em metros (1 unidade = 1 m); snap é função do editor, não do dado |
| `params` | `Dictionary<string, JsonValue>` | validado contra `DeviceTypeDef.paramDefs` (tipo, min/max, default) |

## 3. DeviceTypeDef (catálogo — `catalog/devices/*.json`)

| Campo | Tipo | Regras |
|---|---|---|
| `typeId` | `string` | único no catálogo (ex.: `conveyor.belt.io`, `sensor.photo`, `actuator.piston`) |
| `category` | `enum` | `Passive · Transport · Sensor · Actuator · SourceSink · Part · Hmi` |
| `displayName` | `string` | nome no editor |
| `ports` | `List<PortDef>` | sensores: exatamente 1 porta `In`; atuadores: exatamente 1 porta `Out` (Artigo VI.1). Dispositivos passivos: 0 portas |
| `paramDefs` | `List<ParamDef>` | nome, tipo (`bool/int/float/enum`), default, min/max |
| `visualScene` | `string` | caminho do `.tscn` de apresentação (só camada 4 usa) |
| `behavior` | `string` | chave do comportamento em `Forja.Core.Devices` (factory data-driven — Artigo III.2) |

Catálogo v1 = os 16 tipos do RF-03. Adicionar tipo novo = novo JSON + (se
comportamento inédito) nova classe em Core; o **editor** não recompila.

## 4. PortDef / IoTag

```csharp
record PortDef(string PortName, IoDirection Direction);   // ex.: ("detect", In)
enum IoDirection { In, Out }                               // In = sensor→PLC, Out = PLC→atuador

record IoTag(
    uint DeviceId,
    string PortName,
    IoAddress Address);

record IoAddress(IoArea Area, ushort Offset);
enum IoArea { DiscreteInput, Coil, InputRegister, HoldingRegister }  // v1 usa só os 2 primeiros (digital-only)
```

**Validações (`IoMapValidator`, camada 1 — puras):**
- V1: `(Area, Offset)` único no mapa — duplicado ⇒ `ValidationError` citando os **dois** `DeviceId` (RF-05).
- V2: direção do endereço compatível com a porta (`In` ⇒ `DiscreteInput`; `Out` ⇒ `Coil`).
- V3: todo dispositivo com porta tem tag; tag órfã (device inexistente) ⇒ erro.
- Erros de validação **bloqueiam a transição Edit→Run** (Artigo VI.3).

**Exibição:** UI mostra notação IEC derivada + endereço cru: `%IX0.3 (DI 3)`,
`%QX1.0 (Coil 8)`. Armazenamento canônico = `IoAddress` cru (decisão Q2).

## 5. ConnectionConfig

| Campo | Tipo | Default | Regras |
|---|---|---|---|
| `driver` | `string` | `"null"` | chave do driver (`"null"`, `"modbus-tcp-server"`, `"modbus-tcp-client"`) — troca é config, não recompilação (Artigo IV.2) |
| `bindAddress` | `string` | `"0.0.0.0"` | usado pelo driver servidor (R3) |
| `host` | `string` | `"127.0.0.1"` | usado pelo driver cliente — IP do PLC |
| `port` | `ushort` | `502` | servidor: porta de escuta; cliente: porta do PLC |
| `unitId` | `byte` | `1` | unit/slave id Modbus |
| `timeoutMs` | `int` | `1000` | > 0; timeout de I/O explícito (Artigo VII.2) |

## 6. Part (peça em runtime — `Forja.Core`)

| Campo | Tipo | Regras |
|---|---|---|
| `id` | `uint` (EntityId) | mesmo espaço de id das entidades, ordem estável |
| `kind` | `PartKind { size: S/M/L, material: Metal/Plastic }` | do emissor |
| `body` | handle do rigidbody Jolt | destruído ao sair dos limites do mundo (RF-04) — sem vazamento |

Peças **não são salvas** no `.forja` (estado de runtime); salvar em Run
captura apenas a configuração da cena. (Simplificação v1 registrada.)

## 7. IoTable (snapshot por tick — `Forja.Core`)

Dois bitmaps ordenados por endereço: `inputs` (escrito pelos sensores no fim
do tick N) e `outputs` (lido pelos atuadores no início do tick N+1). O
`IPlcDriver` troca exatamente esses snapshots — nunca acesso direto de
dispositivo a driver. Forçar valor (RF-05) grava numa máscara de override que
prevalece sobre o driver até ser solta.

## 8. Máquina de modos (RF-01)

```
Edit ──validar(ioMap)──▶ Run ◀──▶ Pause ──Step──▶ (Run por 1 tick) ──▶ Pause
  ▲                       │
  └──── Stop (qualquer) ──┘
```

| Transição | Guarda / Efeito |
|---|---|
| Edit → Run | `IoMapValidator` PASS; física ativada; driver `Start()` |
| Run → Pause | congela tick; física dorme; estado inspecionável |
| Pause → Run | retoma **sem** salto de física (não acumula delta) |
| Pause → Step | executa exatamente 1 tick e volta a Pause |
| * → Edit | descarta peças de runtime; física parada |
| Driver `Faulted` | força Run → Pause + sinaliza UI (Artigo VII.1) |

Edição de cena só é permitida em Edit; comandos de edição em outros modos são
rejeitados (Artigo II.2 — UI nunca muda estado de simulação diretamente).

## 9. StateHash (RNF-03)

Entrada canônica por tick, nesta ordem: `tickNumber` → dispositivos (por id:
estado discreto + params dinâmicos) → peças (por id: pos/rot/vel quantizados —
mm, mrad, mm/s) → `inputs`/`outputs` bitmaps. FNV-1a 64-bit (ver research R5).
