---

description: "Task list — Fase 2: sinais analógicos"
---

# Tasks: Sinais analógicos

**Input**: Design documents from `specs/003-analogico/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/)

**Tests**: incluídos — o Artigo V da constitution torna teste headless
obrigatório para todo comportamento de simulação. Não são opcionais aqui.

**Organization**: por user story, em ordem de prioridade. A **Fase 2
(Foundational)** é o gate da camada 1 (o cano de palavras): nenhuma US começa
antes dela verde, com o teste de determinismo passando.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: paralelizável (arquivo diferente, sem dependência pendente)
- **[Story]**: US1–US4 (mapeia à spec.md); Setup/Foundational/Polish sem rótulo

## Path Conventions

Projeto de 4 camadas (constitution): `src/Forja.Anvil`, `src/Forja.Core`,
`src/Forja.Bellows`, `src/Forja.Studio`; testes em `tests/Forja.*.Tests`;
catálogo de dados em `catalog/devices/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: preparar terreno; o projeto e as 4 camadas já existem.

- [x] T001 Confirmar build/testes verdes na `main` antes de tocar camada 1: `dotnet build` + `dotnet test`
- [ ] T002 [P] Criar cena de fixture v1 real em `tests/Forja.Core.Tests/fixtures/` copiando uma cena digital da biblioteca (base do teste de migração US4)

---

## Phase 2: Foundational (Camada 1 — o cano de palavras) 🔒 GATE

**Purpose**: fiar palavras pelo contrato, pela IoTable e pelo driver. Bloqueia
TODAS as user stories. Só fecha com o teste de determinismo verde (Artigo I.4).

**⚠️ CRITICAL**: nenhuma US pode começar antes deste checkpoint.

### Contrato e catálogo (Anvil, camada 1)

- [x] T003 [P] Adicionar enum `PortType { Bool, Word }` e estender `PortDef` com `Type = PortType.Bool` em `src/Forja.Anvil/Catalog/DeviceTypeDef.cs`
- [x] T004 [P] Adicionar `ReadOnlyMemory<ushort> Words` ao `IoSnapshot` e ajustar `Empty(tick)` em `src/Forja.Anvil/Contracts/IPlcDriver.cs` (contrato W1/W2)
- [x] T005 [P] Adicionar record `AnalogScale(ushort RawMin=0, ushort RawMax=65535)` e campo opcional `Scale` ao `IoTag` em `src/Forja.Anvil/Scene/IoTypes.cs`
- [x] T006 Subir `CurrentSchemaVersion` de 1 para 2 em `src/Forja.Anvil/Scene/SceneDocument.cs`

### Conversão e canal de palavras (Core, camada 2)

- [x] T007 Adicionar buffers `ushort[]` de entrada/saída e força numérica (`_inputWords`, `_outputWords`, `_forcedInputWords`, `_forcedOutputWords`) ao `src/Forja.Core/Io/IoTable.cs`
- [x] T008 Implementar a conversão EU↔bruto saturante (`SetInputWord`, `GetOutputWord`, `ForceWord`) na fronteira `IoTable`, lendo `euMin`/`euMax` do tipo e a `AnalogScale` do ponto (contrato scaling-eu-raw S1–S4)
- [x] T009 Preencher `Words` em `BuildInputSnapshot` e aplicar `snapshot.Words` em `ApplyOutputSnapshot` (respeitando `!Valid`) em `src/Forja.Core/Io/IoTable.cs`
- [x] T010 Hashear `_inputWords`/`_outputWords` em ordem de endereço no `WriteState` do `src/Forja.Core/Io/IoTable.cs` (Artigo I.4, contrato W6)
- [x] T011 Estender `IoPointView` com valor numérico/unidade e populá-lo em `BuildView` no `src/Forja.Core/Io/IoTable.cs`

### Migração de schema (Core, camada 2)

- [x] T012 Implementar e registrar `MigrationV1ToV2` (aditiva, só carimba versão) na cadeia `SceneSerializer.Migrations` em `src/Forja.Core/Persistence/SceneSerializer.cs` (research R4)

### Validação (Anvil, camada 1)

- [x] T013 Remover o bloco `analog-not-supported` e implementar a matriz direção×área×tipo (`type-area-mismatch`) + regra `invalid-scale` em `src/Forja.Anvil/Validation/IoMapValidator.cs` (contrato V1–V3)

### Driver (Bellows, camada 3)

- [x] T014 Fiar os `RegisterSource` do `src/Forja.Bellows/Modbus/MirrorDataStore.cs`: input registers como double-buffer (`PublishInputWords`) e holding registers com `CopyHolding(dest)` por tick (research R5)
- [x] T015 Passar `inputs.Words` e devolver holding registers no `Words` de saída no `Exchange` de `src/Forja.Bellows/Modbus/ModbusTcpServerDriver.cs` (contrato W2/W5)
- [x] T016 Ler/escrever input e holding registers do master remoto no modo cliente `src/Forja.Bellows/Modbus/ModbusTcpClientDriver.cs`

### Testes do gate (o que fecha a fase)

- [x] T017 [P] Testes de escala em `tests/Forja.Core.Tests/AnalogScaleTests.cs`: tabela `(euMin,euMax,rawMin,rawMax,eu)→raw` (meio, fundo, topo, fora-da-faixa satura) e round-trip estável (contrato S2/S3)
- [x] T018 [P] Testes da matriz de validação em `tests/Forja.Anvil.Tests/IoMapValidatorTests.cs`: uma célula inválida por linha + `invalid-scale` + `duplicate-address` com dois `%IW0`
- [x] T019 **Teste de determinismo** em `tests/Forja.Core.Tests/`: cena com um ponto analógico rodada 2× com mesmo seed/entradas por N ticks → hash idêntico (Artigo I.4). **Critério de fechamento do gate.**
- [x] T020 [P] Teste do canal de palavras no driver em `tests/Forja.Bellows.Tests/`: publica input register conhecido e lê de volta; escreve holding register e o tick seguinte enxerga (defasagem ≤ 1 tick)

**Checkpoint**: cano de palavras verde ponta a ponta no núcleo + determinismo. Os 18 dispositivos digitais continuam passando (SC-007). US podem começar.

---

## Phase 3: User Story 1 — Ler um valor analógico (Priority: P1) 🎯 MVP

**Goal**: um sensor de nível publica um valor que o CLP lê em `%IW` e escala para EU.

**Independent Test**: montar cena com sensor de nível de grandeza conhecida, rodar N ticks, assertar o bruto no input register; ponta a ponta, ler `%IW` no OpenPLC v4 e reconstruir a EU.

- [ ] T021 [P] [US1] Cenário headless do sensor de nível em `tests/Forja.Core.Tests/LevelSensorTests.cs`: 0/50/100 cm → 0/~32767/65535; fora-da-faixa satura; dois cartões diferentes → brutos diferentes (AS1–AS4)
- [ ] T022 [US1] Implementar o comportamento `LevelSensor : DeviceBehavior` (grandeza física → `SetInputWord(Id, "level", eu)`, `WriteState`) em `src/Forja.Core/Devices/Sensors.cs`
- [ ] T023 [US1] Registrar o comportamento na `src/Forja.Core/Devices/DeviceFactory.cs`
- [ ] T024 [P] [US1] Catálogo `catalog/devices/sensor.level.json` (porta `level` Word/In, params `euMin`/`euMax`, `VisualScene`)
- [ ] T025 [US1] Validar ponta a ponta no OpenPLC v4 (quickstart US1) e registrar o `.st` de leitura/escala em `plc/`

**Checkpoint**: entrada analógica demonstrável sozinha — MVP da fase.

---

## Phase 4: User Story 2 — Comandar um atuador por setpoint (Priority: P2)

**Goal**: um atuador de velocidade variável obedece a um setpoint que o CLP escreve em `%QW`.

**Independent Test**: forçar setpoints crescentes no `%QW` via tabela manual e assertar a velocidade escalando monotonicamente; ponta a ponta, o CLP escreve `%QW` e a sim lê no tick seguinte.

- [ ] T026 [P] [US2] Cenário headless da esteira VV em `tests/Forja.Core.Tests/VariableSpeedConveyorTests.cs`: setpoint 0 → parada, meio → ~1 m/s, fundo → máx; monotônico
- [ ] T027 [US2] Implementar `VariableSpeedConveyor` (lê `GetOutputWord(Id, "speed")` e aplica à velocidade) reusando a base de `src/Forja.Core/Devices/Conveyors.cs`
- [ ] T028 [US2] Registrar o comportamento na `src/Forja.Core/Devices/DeviceFactory.cs`
- [ ] T029 [P] [US2] Catálogo `catalog/devices/conveyor.belt.vspeed.json` (porta `speed` Word/Out, params `euMin`/`euMax`)
- [ ] T030 [US2] Validar ponta a ponta no OpenPLC v4 (quickstart US2) — CLP escreve `%QW`, sim reflete ≤ 1 tick

**Checkpoint**: saída analógica demonstrável sozinha; entrada (US1) intacta.

---

## Phase 5: User Story 3 — Controle de nível ponta a ponta (Priority: P3)

**Goal**: cena sensor de nível (`%IW`) → comparação/setpoint em ST → atuador (`%QW`), validada no OpenPLC v4 — a primeira malha da Forja que age sobre um número.

**Independent Test**: rodar a malha; abaixo do setpoint corrige numa direção, acima na outra; hash idêntico em duas execuções.

- [ ] T031 [US3] Montar a cena de controle de nível em `plc/` (sensor de nível + esteira/atuador VV + mapa de I/O `%IW0`/`%QW0`)
- [ ] T032 [US3] Escrever o programa ST de controle de nível (ler `%IW0`, reescalar, comparar com setpoint, escrever `%QW0`) na biblioteca `plc/`
- [ ] T033 [US3] Cenário headless da malha em `tests/Forja.Core.Tests/`: abaixo/acima do setpoint → direção correta; sem oscilar por quantização (AS1/AS2)
- [ ] T034 [US3] Teste de determinismo da malha completa (2× mesmo seed/entradas → mesmo hash) e validação no OpenPLC v4 (quickstart US3)

**Checkpoint**: malha analógica fechada — a fatia "vendável" da fase.

---

## Phase 6: User Story 4 — Carregar cena v1 sem trauma (Priority: P2)

**Goal**: cena `schemaVersion: 1` carrega numa Forja v2 com campos padrão, sem migração destrutiva; campo v2 malformado falha explícito.

**Independent Test**: carregar a fixture v1 (T002) → sucesso e comportamento idêntico à Fase 1; carregar cena v2 com campo desconhecido → erro com caminho+motivo.

- [ ] T035 [P] [US4] Teste de carga aditiva em `tests/Forja.Core.Tests/`: fixture v1 → `Load` sucesso, `scale` = null, `PortType` = Bool por default (AS1)
- [ ] T036 [P] [US4] Teste round-trip em `tests/Forja.Core.Tests/`: carregar v1, salvar, recarregar → válida com campos aditivos e defaults explícitos (AS2)
- [ ] T037 [US4] Teste negativo em `tests/Forja.Core.Tests/`: cena v2 com campo analógico desconhecido → falha `UnmappedMemberHandling.Disallow` com caminho+motivo (AS3)

**Checkpoint**: migração aditiva provada; zero regressão digital (SC-005).

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T038 [P] Terceiro dispositivo analógico: `Scale : DeviceBehavior` (balança — soma peso das peças → input register) em `src/Forja.Core/Devices/Sensors.cs` + catálogo `catalog/devices/sensor.scale.json` + cenário headless (prova que o canal de entrada é genérico, research R8)
- [ ] T039 [US1] [US2] UI: `src/Forja.Studio/UI/IoTablePanel.cs` exibe número+unidade e força valor numérico em ponto analógico (FR-019, FR-020)
- [ ] T040 [P] Teste de arquitetura: confirmar que `float` (EU) não vaza para a camada 1/3 em `tests/Forja.Architecture.Tests/LayerRulesTests.cs`
- [ ] T041 [P] Atualizar `ROADMAP.md` (Fase 2 em progresso→fechada) e a memória de estado do projeto
- [ ] T042 Rodar a validação completa do `quickstart.md` (US1–US4) e fechar a spec

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências.
- **Foundational (Phase 2)**: depende do Setup; **BLOQUEIA todas as US**. Fecha em T019 (determinismo) verde.
- **US1 (Phase 3)**: depende do gate. É o MVP.
- **US2 (Phase 4)**: depende do gate; independente de US1.
- **US3 (Phase 5)**: depende do gate; **integra US1+US2** (usa os dois dispositivos).
- **US4 (Phase 6)**: depende do gate (schema já em v2 por T006/T012); independente de US1–US3.
- **Polish (Phase 7)**: depende das US desejadas prontas.

### Within Foundational (ordem interna)

- T003–T006 (contrato/tipos) antes de T007–T011 (IoTable usa os tipos).
- T007→T008→T009/T010/T011 (buffers antes de conversão antes de snapshot/hash/view).
- T013 (validação) depende de T003/T005 (PortType/Scale).
- T014→T015 (store antes do Exchange do servidor).
- T017–T020 (testes) depois do código que exercitam; T019 é o último a fechar.

### Parallel Opportunities

- Setup: T002 [P].
- Foundational: T003/T004/T005 [P] (arquivos diferentes da camada 1); T017/T018/T020 [P] entre si.
- US1: T021 e T024 [P]; US2: T026 e T029 [P]; US4: T035/T036 [P].
- Depois do gate, US1, US2 e US4 podem correr em paralelo (times diferentes); US3 espera US1+US2.

---

## Implementation Strategy

### MVP First (US1)

1. Phase 1: Setup.
2. Phase 2: Foundational — o gate da camada 1 (fecha em T019 verde).
3. Phase 3: US1 — sensor de nível lido em `%IW`.
4. **PARAR e VALIDAR** US1 no OpenPLC v4 (quickstart US1). Demo.

### Incremental Delivery

Gate → US1 (entrada) → US2 (saída) → US3 (malha) → US4 (migração) → Polish.
Cada US é uma demo independente; US3 junta as duas metades na aplicação de
controle de nível.

### Estimativa

5–8 sessões (rascunho/ADR 0005). O gate (Phase 2) é o maior bloco e o mais
arriscado (determinismo do hash); US3 é a fatia demonstrável da fase.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Todo comportamento novo fecha com cenário headless (Artigo V) antes de ser considerado pronto.
- Commit por tarefa ou grupo lógico; parar em cada checkpoint para validar a US isolada.
- Não subir a camada 2 antes de T019 (determinismo) verde — é o modo de falha silenciosa que o Artigo I existe para pegar.
