# Contrato — Mapeamento Modbus (v1, digital-only)

A Forja opera como **servidor** (default, usado pela demo OpenPLC) ou
**cliente** Modbus TCP, conforme `connection.driver` (research R3).
Digital-only na v1 — `inputRegister`/`holdingRegister` existem no schema, mas
são rejeitados pelo validador v1.

## Modo servidor (`modbus-tcp-server`) — PLC é master

| Área Modbus (local) | Function codes (master) | Direção Forja | Porta | Notação IEC na UI |
|---|---|---|---|---|
| Discrete Inputs | FC02 (read) | sensores → PLC | `In` | `%IX{off/8}.{off%8}` |
| Coils | FC01 (read), FC05/FC15 (write) | PLC → atuadores | `Out` | `%QX{off/8}.{off%8}` |

## Modo cliente (`modbus-tcp-client`) — Forja é master

O cliente não pode escrever discrete inputs remotos (limite do protocolo);
os dois sentidos usam coils do PLC, em offsets que o usuário casa com o
programa do PLC:

| Operação Forja | Function code | Direção | Porta |
|---|---|---|---|
| `WriteMultipleCoils` em `inputBaseOffset + offset da tag` | FC15 | sensores → PLC | `In` |
| `ReadCoils` em `0..N-1` (offset da tag) | FC01 | PLC → atuadores | `Out` |

`connection.inputBaseOffset` (default 0) desloca a janela de escrita dos
sensores para não colidir com as coils lidas pelos atuadores — ex.: com
`inputBaseOffset: 100`, o sensor da tag coil 3 vira a coil remota 103.

No modo cliente a validação V2 aceita porta `In` mapeada em `coil` (área
remota); a UI exibe o endereço remoto efetivo (`PLC coil 103`).

Exibição na UI (decisão Q2): `%IX0.3 (DI 3)` · `%QX1.0 (Coil 8)`.
Armazenamento canônico: `{ "area": "...", "offset": n }` cru.

## Sincronização com o tick (RNF-02, Artigo VI)

```
tick N:   dispositivos atualizam → sensores gravam IoTable.inputs
          └─ fim do tick: driver publica inputs no data-store Modbus
tick N+1: início: driver entrega snapshot de coils → IoTable.outputs
          atuadores leem outputs e agem
```

- Escrita de coil pelo master entre ticks é aplicada no início do tick
  seguinte (nunca no meio de um tick — determinismo, Artigo I).
- Latência alvo: evento de sensor visível ao master em < 20 ms
  (tick 16,7 ms + publicação imediata).
- Override manual (RF-05 "forçar") prevalece sobre o valor do master até ser
  liberado; UI indica bit forçado.

## Validação (bloqueia Edit→Run)

1. `(area, offset)` duplicado ⇒ erro citando os dois dispositivos (RF-05).
2. Porta `In` com área ≠ `discreteInput` ⇒ erro (idem `Out`/`coil`).
3. Offset > 65535 ⇒ erro de schema.

## Cena demo (RF-09) — mapa de referência

| Dispositivo | Porta | Endereço | IEC |
|---|---|---|---|
| Esteira principal | run | coil 0 | `%QX0.0` |
| Sensor de altura | detect | DI 0 | `%IX0.0` |
| Pistão desviador | extend | coil 1 | `%QX0.1` |
| Sensor fim de curso do pistão | extended | DI 1 | `%IX0.1` |
| Botão start (HMI) | pressed | DI 2 | `%IX0.2` |
| Luz indicadora (HMI) | on | coil 2 | `%QX0.2` |

O programa OpenPLC de exemplo (`demo/openplc/separador.st`) usa exatamente
estes endereços via *Slave Devices* apontando para a Forja.
