# Tasks: Forja v1 — Simulador de Plantas Industriais

**Input**: Design documents from `/specs/001-forja-v1/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, constitution 1.0.0

**Tests**: OBRIGATÓRIOS — Artigo V da constitution exige teste headless para todo
comportamento de simulação; os aceites da spec são executáveis.

**Organization**: fatias verticais (Artigo VIII) — cada user story é um
incremento ponta a ponta demonstrável, com critério de aceite verificável.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: paralelizável (arquivos distintos, sem dependência pendente)
- **[Story]**: US1–US5 (fatias abaixo)

## User stories (fatias verticais)

| Story | Prioridade | Fatia | RFs cobertos |
|---|---|---|---|
| US1 | P1 🎯 MVP | **Esteira determinística**: caixa emitida anda numa esteira, cai na calha; hash de determinismo prova RNF-03 | RF-01 (parcial), RF-04, RNF-03 |
| US2 | P2 | **Modo manual ponta a ponta**: sensor + pistão + Tabela de I/O com forçar, driver nulo | RF-05, RF-07, RF-01 |
| US3 | P3 | **Editor de cena**: montar planta do zero, undo/redo, salvar/carregar | RF-02, RF-08 |
| US4 | P4 | **Catálogo completo v1**: os 16 dispositivos, cada um com teste | RF-03 |
| US5 | P5 | **PLC de verdade**: Modbus TCP + cena demo + OpenPLC | RF-06, RF-09 |

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: projeto Godot + solution em camadas + CI. Constitution: Artigos I.1, II, V.3.

- [x] T001 Criar projeto Godot 4.4 .NET na raiz: `project.godot` (Jolt Physics, `physics_ticks_per_second=60`, forward_plus), `.godot-version` pinando a versão exata, `.gitignore` (`.godot/`, `build/`, `.claude/*.local.json` — nunca `.claude/` inteira)
- [x] T002 Criar `Forja.sln` com grafo de camadas: `src/Forja.Anvil/Forja.Anvil.csproj` (net8.0, zero refs), `src/Forja.Core/Forja.Core.csproj` (ref Anvil), `src/Forja.Bellows/Forja.Bellows.csproj` (ref Anvil+Core, NuGet NModbus), `src/Forja.Studio/Forja.Studio.csproj` (csproj do jogo Godot, ref todos) e projetos de teste `tests/Forja.{Anvil,Core,Bellows,Architecture}.Tests/*.csproj` (xUnit)
- [x] T003 [P] Configurar `.editorconfig` + analyzers .NET (regra bloqueando `catch` vazio — Artigo VII.3; TreatWarningsAsErrors nas camadas 1–3)
- [x] T004 [P] Testes de arquitetura em `tests/Forja.Architecture.Tests/LayerRulesTests.cs` (NetArchTest): Anvil sem refs; Core não usa `Godot.Control`/`Godot.Input`/`CanvasItem`; Bellows não usa `Godot.*` de UI; violação = build vermelho (Artigo II)
- [x] T005 [P] CI GitHub Actions em `.github/workflows/ci.yml` (runner windows): `dotnet build` + `dotnet test` + download Godot headless + GdUnit4; build vermelho não mergeia (Artigo V.3)

**Checkpoint**: `dotnet test` verde, `godot --headless --import` roda, CI configurado.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: contratos, schema, loop e I/O que TODAS as fatias usam. Fonte: data-model.md, contracts/.

**⚠️ CRITICAL**: nenhuma user story antes de concluir esta fase.

- [x] T006 Contratos em `src/Forja.Anvil/Contracts/`: `IPlcDriver.cs`, `DriverState.cs`, `IoSnapshot.cs` (conforme contracts/iplcdriver.md), `IRandomSource.cs`, `ISimCommand.cs`, `Result.cs`
- [x] T007 [P] Tipos de cena em `src/Forja.Anvil/Scene/`: `SceneDocument.cs`, `DeviceInstance.cs`, `Pose.cs`, `IoTag.cs`, `IoAddress.cs` (+ `IoArea`, `IoDirection`), `ConnectionConfig.cs` (defaults do data-model §5)
- [x] T008 [P] Tipos de catálogo em `src/Forja.Anvil/Catalog/`: `DeviceTypeDef.cs`, `PortDef.cs`, `ParamDef.cs` + loader de `catalog/devices/*.json` com erro explícito para typeId duplicado
- [x] T009 `IoMapValidator` em `src/Forja.Anvil/Validation/` (regras V1–V3 do data-model §4, erro cita os dois DeviceId) + testes em `tests/Forja.Anvil.Tests/IoMapValidatorTests.cs`
- [x] T010 Serialização em `src/Forja.Core/Persistence/`: `SceneSerializer.cs` (System.Text.Json estrito, regras S1–S8 de contracts/forja-schema.md), `ISceneMigration.cs` + pipeline; testes round-trip `load(save(doc))==doc` e erros de versão em `tests/Forja.Core.Tests/SceneSerializerTests.cs`
- [x] T011 Loop e modos em `src/Forja.Core/Loop/`: `SimulationLoop.cs` (registro de entidades ordenado por EntityId — Artigo I.3), `SimMode.cs` + máquina de transições do data-model §8 (guardas, Step = exatamente 1 tick), fila de `ISimCommand`; `SeededRandom.cs` em `src/Forja.Core/State/`; testes da máquina de modos em `tests/Forja.Core.Tests/SimModeTests.cs`
- [x] T012 [P] `StateHasher.cs` (FNV-1a 64, quantização mm/mrad — research R5) em `src/Forja.Core/State/` + testes de sensibilidade/estabilidade em `tests/Forja.Core.Tests/StateHasherTests.cs`
- [x] T013 [P] `IoTable.cs` em `src/Forja.Core/Io/` (bitmaps inputs/outputs ordenados por endereço, máscara de override para "forçar", troca com `IPlcDriver` no fim/início do tick — contracts/modbus-mapping.md) + testes em `tests/Forja.Core.Tests/IoTableTests.cs`
- [x] T014 [P] `NullDriver.cs` em `src/Forja.Bellows/Null/` (regras C1–C5, nunca Faulted) + testes de contrato em `tests/Forja.Bellows.Tests/NullDriverTests.cs`
- [x] T015 Bootstrap Studio: `src/Forja.Studio/Main.tscn` + `Main.cs` ligando `_PhysicsProcess` → `SimulationLoop.Tick()` (única fonte de tick — Artigo I.1), carregamento de cena `.forja` por caminho; roda em `godot --headless`
- [x] T016 Instalar GdUnit4 em `addons/gdUnit4/`, criar `tests/Forja.Headless.Tests/` com smoke test (boot headless + carregar cena vazia) e plugar no CI

**Checkpoint**: fundação pronta — contratos, schema, loop, I/O e ambos os runners de teste verdes.

---

## Phase 3: User Story 1 — Esteira determinística (Priority: P1) 🎯 MVP

**Goal**: caixa emitida percorre esteira e cai na calha, 100% dirigido por cena
`.forja`, com determinismo provado por hash (o risco nº 1 da spec §6 é atacado
primeiro, conforme pedido).

**Independent Test**: quickstart V-A e V-B — headless, sem PLC, sem editor.

### Tests for User Story 1 (escrever primeiro, ver falhar)

- [x] T017 [P] [US1] Teste headless RF-04-a em `tests/Forja.Headless.Tests/ConveyorFlowTest.cs`: montar cena mínima (emissor→esteira→calha→sink) → 600 ticks → caixa transportada e removida no sink
- [x] T018 [P] [US1] `DeterminismTest.cs` em `tests/Forja.Headless.Tests/`: mesma cena + seed 42 + script de inputs, 10.000 ticks, 2 execuções → hashes idênticos; ao falhar, reporta primeiro tick divergente (Artigo I.4, RNF-03)

### Implementation for User Story 1

- [x] T019 [P] [US1] Comportamentos em `src/Forja.Core/Devices/`: `StaticBody.cs` (piso/calha), `ConveyorBelt.cs` (velocidade constante, bidirecional — fricção por surface velocity Jolt), catálogo `catalog/devices/floor.json`, `chute.json`, `conveyor.belt.json`
- [x] T020 [P] [US1] `PartBody.cs` (caixa S/M/L, metal/plástico — massa e material físico) + `Emitter.cs` (intervalo fixo via tick count, qtd máx, usa `IRandomSource`) + `Sink.cs` em `src/Forja.Core/Devices/` + catálogos `part.box.json`, `emitter.json`, `sink.json`
- [x] T021 [US1] Integração física em `src/Forja.Core/Physics/PhysicsWorld.cs`: criação/destruição de corpos via PhysicsServer3D (headless-safe), kill-zone nos limites do mundo destruindo peça sem vazamento (RF-04)
- [x] T022 [US1] `DeviceFactory.cs` em `src/Forja.Core/Devices/` mapeando `DeviceTypeDef.behavior` → classe (data-driven, Artigo III.2); erro explícito para behavior desconhecido
- [x] T023 [US1] Visual em `src/Forja.Studio/Rendering/`: `DeviceView.cs` sincronizando nós Godot do estado do core (leitura apenas — Artigo II.2) + `assets/devices/{floor,chute,conveyor,box,emitter,sink}.tscn`
- [x] T024 [US1] Toolbar de modos em `src/Forja.Studio/UI/ModeToolbar.cs` (Edit/Run/Pause/Step chamando `ISimCommand`) + teste headless de transições em `tests/Forja.Headless.Tests/SimModeE2ETest.cs`: Pause→Run sem salto de física, Step avança exatamente 1 tick (RF-01)
- [x] T025 [US1] Mini-cena `demo/esteira-minima.forja` (emissor→esteira→calha→sink) validando carga por arquivo; T017/T018 ficam verdes

**Checkpoint**: MVP — `godot --headless` prova RF-04-a + determinismo; app abre e roda a mini-cena visualmente.

---

## Phase 4: User Story 2 — Modo manual ponta a ponta (Priority: P2)

**Goal**: sensor fotoelétrico + pistão operados pela Tabela de I/O com driver
nulo — a planta funciona sem PLC (RF-07), com validação de endereço bloqueante.

**Independent Test**: quickstart V-C e V-D.

### Tests for User Story 2

- [x] T026 [P] [US2] Teste headless em `tests/Forja.Headless.Tests/SensorActuatorTest.cs`: caixa interrompe feixe → DI 0 = 1 no mesmo tick; forçar coil do pistão → pistão estende e empurra caixa sem atravessar (RF-04-c)
- [x] T027 [P] [US2] Teste de validação em `tests/Forja.Anvil.Tests/DuplicateAddressTests.cs`: dois devices no mesmo `(area,offset)` → Run bloqueado, erro cita ambos (RF-05, Artigo VI.3)

### Implementation for User Story 2

- [x] T028 [P] [US2] `PhotoSensor.cs` (raycast do feixe, alcance configurável) em `src/Forja.Core/Devices/` + `catalog/devices/sensor.photo.json`
- [x] T029 [P] [US2] `Piston.cs` (extend/retract cinemático com curso e velocidade, porta out `extend` + colisor que empurra) em `src/Forja.Core/Devices/` + `catalog/devices/actuator.piston.json`
- [x] T030 [US2] Ciclo completo de I/O no `SimulationLoop`: fim do tick sensores→IoTable→driver.Exchange; início do tick outputs→atuadores (contracts/modbus-mapping.md, sincronização determinística)
- [x] T031 [US2] Tabela de I/O em `src/Forja.Studio/UI/IoTablePanel.cs`: lista devices de I/O, endereço em notação dupla `%IX0.0 (DI 0)`, valor ao vivo, direção; reatribuir endereço (comando de edição); **forçar** bit com indicação visual (RF-05)
- [x] T032 [US2] Bloqueio Edit→Run com `IoMapValidator` + diálogo de erro apontando os dois dispositivos em `src/Forja.Studio/UI/ValidationDialog.cs`
- [x] T033 [P] [US2] HMI em `src/Forja.Core/Devices/`: `PushButton.cs`, `SelectorSwitch.cs` (→ DI), `IndicatorLight.cs` (← coil) + catálogos + interação de clique em `src/Forja.Studio/UI/HmiInteraction.cs` + teste headless em `tests/Forja.Headless.Tests/HmiTest.cs`

**Checkpoint**: cena com sensor+pistão operável só pela Tabela de I/O, sem PLC (RF-07 aceite).

---

## Phase 5: User Story 3 — Editor de cena (Priority: P3)

**Goal**: montar a planta do zero no editor: catálogo, posicionamento com snap,
undo/redo ≥ 50, salvar/carregar round-trip.

**Independent Test**: quickstart V-F e V-G.

### Tests for User Story 3

- [ ] T034 [P] [US3] Testes de undo/redo em `tests/Forja.Core.Tests/EditorCommandTests.cs`: cada `IEditorCommand` Do/Undo restaura o `SceneDocument` exatamente; pilha de 100 níveis; comandos rejeitados fora do modo Edit
- [ ] T035 [P] [US3] Teste round-trip de editor em `tests/Forja.Headless.Tests/EditorRoundTripTest.cs`: montar cena por comandos → salvar → carregar → igualdade estrutural (RF-08)

### Implementation for User Story 3

- [ ] T036 [US3] `IEditorCommand.cs` + `UndoRedoStack.cs` (100 níveis) em `src/Forja.Studio/Commands/` com comandos `PlaceDevice`, `MoveDevice`, `RotateDevice`, `DeleteSelection`, `DuplicateSelection`, `EditParam`, `ReassignAddress` — todos operando no `SceneDocument` (research R7)
- [ ] T037 [US3] Painel de catálogo em `src/Forja.Studio/Editor/CatalogPanel.cs` listando `catalog/devices/*.json` em runtime (Artigo III.2) com preview e colocação por clique
- [ ] T038 [US3] Seleção e gizmos em `src/Forja.Studio/Editor/`: `SelectionManager.cs`, `MoveGizmo.cs`/`RotateGizmo.cs` com snap de grid (0,1 m) e angular (15°), câmera orbital
- [ ] T039 [US3] Painel de parâmetros em `src/Forja.Studio/Editor/ParamsPanel.cs` gerado de `DeviceTypeDef.paramDefs` (data-driven, sem UI hardcoded por tipo)
- [ ] T040 [US3] Salvar/Carregar em `src/Forja.Studio/UI/FileDialogs.cs` (`.forja`, erro de carga com caminho+motivo — Artigo VII.3) + cena nova
- [ ] T041 [US3] Validação manual RF-02: montar a cena demo do zero só no editor, documentar em `specs/001-forja-v1/checklists/editor-acceptance.md`

**Checkpoint**: usuário monta, salva e roda a própria planta sem tocar em JSON.

---

## Phase 6: User Story 4 — Catálogo completo v1 (Priority: P4)

**Goal**: todos os 16 tipos do RF-03, cada um colocável, salvável, recarregável
e com teste headless.

**Independent Test**: aceite RF-03 — teste headless por dispositivo.

- [ ] T042 [P] [US4] `ConveyorBeltIo.cs` (liga/desliga por coil) em `src/Forja.Core/Devices/` + `catalog/devices/conveyor.belt.io.json` + teste em `tests/Forja.Headless.Tests/Devices/ConveyorIoTest.cs`
- [ ] T043 [P] [US4] `ProximitySensor.cs` (capacitivo detecta tudo / indutivo só metal — usa `PartKind.material`) + `HeightSensor.cs` (difuso com threshold) + catálogos + testes em `tests/Forja.Headless.Tests/Devices/SensorsTest.cs`
- [ ] T044 [P] [US4] `Pusher.cs` (desviador) + `Stopper.cs` (trava de esteira) + catálogos + testes em `tests/Forja.Headless.Tests/Devices/ActuatorsTest.cs`
- [ ] T045 [P] [US4] Passivos: `guia lateral` e `grade estrutural` (variações de `StaticBody` via params) + catálogos + teste de colisão em `tests/Forja.Headless.Tests/Devices/PassiveTest.cs`
- [ ] T046 [US4] Visuais `.tscn` em `assets/devices/` para todos os tipos novos + entrada no painel de catálogo (verificar que NENHUM exige recompilar o editor — Artigo III.2)
- [ ] T047 [US4] Teste-matriz de persistência em `tests/Forja.Core.Tests/CatalogRoundTripTests.cs`: para cada um dos 16 typeIds — colocar, salvar, recarregar, comparar (aceite RF-03)

**Checkpoint**: catálogo v1 congelado e 100% testado.

---

## Phase 7: User Story 5 — PLC de verdade: Modbus TCP + demo (Priority: P5)

**Goal**: conectar num OpenPLC real e entregar a cena demo separador por altura
com programa ladder incluído.

**Independent Test**: quickstart V-E.

### Tests for User Story 5

- [x] T048 [P] [US5] Testes de contrato em `tests/Forja.Bellows.Tests/ModbusTcpDriverTests.cs` com master NModbus loopback: DI visível < 20 ms (RNF-02); escrita de coil chega no snapshot; desconexão do master → `Faulted` com motivo (regras C1–C4)

### Implementation for User Story 5

- [x] T049 [US5] `ModbusTcpServerDriver.cs` em `src/Forja.Bellows/Modbus/` — servidor Modbus TCP (NModbus, research R3): data-store espelhando IoTable, bind/porta da `ConnectionConfig`, thread de rede desacoplada do tick com handoff sem lock no caminho quente
- [x] T049b [US5] `ModbusTcpClientDriver.cs` em `src/Forja.Bellows/Modbus/` — cliente Modbus TCP (Forja master): conecta em `host:port`, FC15 para sensores, FC01 para atuadores, reconexão com backoff, queda ⇒ `Faulted`; testes contra servidor NModbus loopback em `tests/Forja.Bellows.Tests/ModbusTcpClientDriverTests.cs`
- [ ] T050 [US5] Falha segura no core: `DriverState.Faulted` → Run→Pause + evento para UI; timeout `ConnectionConfig.TimeoutMs` aplicado no `Exchange` (Artigo VII) + teste headless em `tests/Forja.Headless.Tests/FailSafeTest.cs`
- [ ] T051 [US5] UI de conexão em `src/Forja.Studio/UI/ConnectionPanel.cs`: driver (null/modbus-tcp), bind, porta, timeout; indicador Desconectado/Aguardando master/Conectado/Erro (RF-06)
- [ ] T052 [US5] Cena demo `demo/separador-altura.forja` conforme contracts/modbus-mapping.md (emissor S/L → esteira → sensor de altura → pistão → 2 calhas → sinks) montada pelo editor
- [ ] T053 [P] [US5] Programa OpenPLC `demo/openplc/separador.st` usando os endereços do mapa de referência + instruções de configuração *Slave Devices* em `demo/openplc/README.md`
- [ ] T054 [US5] Validação manual V-E com OpenPLC real: sensor→pistão < 100 ms; queda do PLC pausa e sinaliza; registrar resultado em `specs/001-forja-v1/checklists/openplc-acceptance.md`

**Checkpoint**: RF-06 e RF-09 aceitos — produto completo funcionalmente.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T055 Performance RNF-01: cena de estresse com 200 peças, sleep de rigidbody parado, perf test headless medindo tempo de tick < 16,6 ms em `tests/Forja.Headless.Tests/PerfTest.cs`
- [ ] T056 [P] Estabilidade RNF-07 (proxy automatizado): soak de 30 min headless com emissor contínuo + kill-zone, memória final ≤ 105% da inicial em `tests/Forja.Headless.Tests/SoakTest.cs`; soak manual de 8 h antes do release
- [ ] T057 [P] Medir RNF-04/RNF-05 (startup < 5 s; cena 100 devices < 2 s) e otimizar carga (catálogo lazy, `.tscn` precarregados)
- [ ] T058 Export ZIP portátil (research R8): `export_presets.cfg` Windows x64 self-contained + script `build/package.ps1` gerando `Forja-v1-win-x64.zip`; testar em máquina limpa sem .NET (RNF-06)
- [ ] T059 [P] `README.md` do repo (visão, build, screenshot da demo) + revisar `specs/001-forja-v1/quickstart.md` contra o produto final
- [ ] T060 Rodar quickstart V-A…V-G completo e registrar em `specs/001-forja-v1/checklists/release-v1.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)** → **Foundational (P2)** → bloqueia todas as stories
- **US1 (MVP)**: só depende da Foundational
- **US2**: depende de US1 (loop físico + partes) — sensores/atuadores agem sobre peças
- **US3**: depende de US1 (dispositivos para colocar); independe de US2
- **US4**: depende de US2 (padrões de sensor/atuador) e US3 (painel de catálogo)
- **US5**: depende de US2 (ciclo de I/O); a cena demo (T052) depende de US3+US4
- **Polish**: depende de todas

### Parallel Opportunities

- Setup: T003, T004, T005 em paralelo após T002
- Foundational: T007, T008 ∥ após T006; T012, T013, T014 ∥ após T011
- US1: T017, T018 (testes) ∥; T019, T020 ∥
- US2: T026, T027 ∥; T028, T029, T033 ∥
- US4: T042–T045 todos ∥ (arquivos distintos)
- US3 e US2 podem andar em paralelo depois de US1

### Parallel Example: User Story 1

```text
# Depois do checkpoint da Phase 2, lançar juntos:
Task: "T017 Teste headless ConveyorFlowTest.cs"
Task: "T018 DeterminismTest.cs (10k ticks, hash)"
Task: "T019 StaticBody + ConveyorBelt + catálogos"
Task: "T020 PartBody + Emitter + Sink + catálogos"
```

---

## Implementation Strategy

**MVP first (US1)**: Setup → Foundational → US1 → **parar e validar** V-A/V-B
do quickstart. O teste de determinismo (T018) nasce na primeira fatia — se o
Jolt não for determinístico na nossa configuração, descobrimos na semana 1,
não na 10 (mitigação do risco §6 da spec).

**Entrega incremental**: cada checkpoint é demonstrável — US1 (caixa anda),
US2 (planta operável à mão), US3 (usuário monta a sua), US4 (catálogo cheio),
US5 (PLC real). Nenhuma fase entrega "camada horizontal" (Artigo VIII).

## Notes

- Total: **60 tarefas** (T001–T060)
- Aceites da spec mapeados: RF-01→T024 · RF-02→T036-T041 · RF-03→T042-T047 ·
  RF-04→T017/T021/T026 · RF-05→T027/T031/T032 · RF-06→T048-T051/T054 ·
  RF-07→T030-T033 · RF-08→T035/T040/T047 · RF-09→T052-T054 ·
  RNF-01→T055 · RNF-02→T048 · RNF-03→T018 · RNF-04/05→T057 · RNF-06→T058 · RNF-07→T056
- Commit após cada tarefa ou grupo lógico; parar em qualquer checkpoint para validar

---

## Notas de implementação (sessão 2, 2026-07-17)

- **T016**: GdUnit4 substituído por runner headless próprio em
  `src/Forja.Studio/Headless/` (`--forja-tests`, exit code p/ CI) — ver
  research R4 revisado. O diretório `tests/Forja.Headless.Tests/` não existe;
  os cenários vivem no assembly do Studio (o csproj raiz exclui `tests/**`).
- **T017/T018**: implementados como cenários do runner (`ConveyorFlowScenario`,
  `DeterminismScenario` — 2×10.000 ticks, hash idêntico tick a tick, com Jolt
  real). Verdes em 2026-07-17.
- **T020**: `PartBody` virou `PartKind`/`Part` dentro de `PartsManager`
  (peça não é device de catálogo); `part.box.json` não se aplica.
- **T021**: `GodotPhysicsWorld` usa `PhysicsServer3D` puro (RIDs, sem nós).
  Descoberta importante: `Engine.TimeScale` ESCALA o dt passado ao servidor de
  física — aceleração de testes exige compensar com `PhysicsTicksPerSecond`
  (1200 ticks × TimeScale 20 ⇒ dt = 1/60 exato).
- **T049/T049b**: servidor com watchdog de inatividade do master
  (timeout ⇒ Faulted, atividade nova ⇒ Ready); cliente com reconexão em
  backoff (500 ms→5 s). `DriverRegistry` resolve a chave da ConnectionConfig e
  dimensiona buffers por `IoTable.OutputCount`.
