# Rascunho — Fase 2: sinais analógicos (`specs/003-analogico`)

> **Não é a spec.** É o kickoff deixado em 22/07 para dar arranque à próxima
> sessão. Ao retomar: rodar `/speckit-specify` para formalizar isto em `spec.md`,
> e ratificar/ajustar o [ADR 0005](../../adr/0005-sinal-analogico-e-o-contrato-de-io.md)
> antes da primeira linha de código. Custo zero (OpenPLC/.NET/Godot já
> instalados) — [ADR 0002](../../adr/0002-ecossistema-de-custo-zero.md).

## Objetivo da fase

Levar a Forja de **digital-only** para **digital + analógico**: valores de 16
bits no contrato de I/O, escalonamento bruto ↔ unidade de engenharia (o
equivalente a um 4–20 mA), e a primeira lógica de CLP que compara e age sobre
um número, não sobre um bit.

Fatia demonstrável (Artigo VIII): uma cena de **controle de nível** —
sensor de nível analógico → programa ST com **setpoint/comparação** (`%IW`) →
atuador de velocidade variável (`%QW`) — validada ponta a ponta contra o
OpenPLC v4 real.

## Decisões já propostas (ADR 0005 — ratificar)

1. `IoSnapshot` ganha canal de palavras (`ReadOnlyMemory<ushort> Words`) paralelo aos bits.
2. Escalonamento é dado da cena, por ponto analógico (device dá EU; a fronteira converte EU↔bruto; registrador carrega bruto).
3. `PortDef` ganha `PortType { Bool, Word }`; `schemaVersion` vai a 2 de forma aditiva.
4. Palavras entram no hash de determinismo.

## Mapa de trabalho (por camada)

| Camada | Arquivo | Mudança |
|---|---|---|
| Contrato | `Forja.Anvil/Contracts/IPlcDriver.cs` | `IoSnapshot` + canal de palavras |
| Catálogo | `Forja.Anvil/Catalog/DeviceTypeDef.cs` | `PortDef` ganha `PortType` |
| I/O da sim | `Forja.Core/Io/IoTable.cs` | `ushort[]` in/holding, força numérica, `IoPointView` com valor+unidade, `WriteState` hasheia palavras, escala EU↔bruto |
| Driver | `Forja.Bellows/Modbus/MirrorDataStore.cs` | fiar o `RegisterSource` (double-buffer de `ushort` nos input registers; ler holding registers do master de volta ao tick) |
| Driver cliente | `Forja.Bellows/Modbus/*` (modo cliente) | ler/escrever registers do master remoto |
| Validação | `Forja.Anvil/Validation/IoMapValidator.cs` | remover `analog-not-supported`; regra direção×área×tipo |
| Schema | `Forja.Anvil/Scene/SceneDocument.cs` | `CurrentSchemaVersion` 1→2 + carga aditiva de cena v1 |
| UI | `Forja.Studio/UI/IoTablePanel.cs` | mostrar número+unidade; forçar valor numérico |

## Dispositivos analógicos novos

Cada um = comportamento + catálogo JSON + cenário headless (Artigo V):

- **Sensor de nível / distância** — escreve input register a partir de uma grandeza física (altura de peça acumulada, posição).
- **Balança** — peso da(s) peça(s) sobre ela → input register.
- **Esteira de velocidade variável** — lê holding register como setpoint de velocidade.

(Manter o critério de entrada do [ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md):
tipo novo entra quando habilita uma classe de lógica de CLP nova. Analógico +
setpoint é exatamente isso.)

## Histórias de usuário candidatas (para o speckit lapidar)

- **US1** — um sensor analógico publica um valor que o CLP lê em `%IW` e escala para EU.
- **US2** — um atuador de velocidade variável obedece a um setpoint que o CLP escreve em `%QW`.
- **US3** — cena de controle de nível ponta a ponta (sensor → comparação/setpoint → atuador), validada no OpenPLC v4.
- **US4** — cena v1 (digital) carrega sem migração destrutiva numa Forja v2 (prova da carga aditiva).

## Riscos / o que puxa a estimativa

- **Determinismo** — o hash precisa refletir palavras corretamente; é o ponto mais fácil de quebrar em silêncio.
- **Migração** — cena v1 tem que carregar; testar com uma cena real da biblioteca.
- **Hot-path do `Exchange`** — o canal de palavras não pode alocar por tick à toa.

Estimativa: **5–8 sessões** (ver detalhamento na conversa de 22/07 / memória
`estado-atual-pick-and-place`).

## Primeiro passo ao retomar

1. `/speckit-specify` → formaliza este rascunho em `spec.md`.
2. Ratificar o ADR 0005 (mudar Status para Aceito) ou ajustar as decisões.
3. `/speckit-plan` → `plan.md` + `tasks.md`, começando pelo contrato (`IoSnapshot`) como o gate de camada 1.
