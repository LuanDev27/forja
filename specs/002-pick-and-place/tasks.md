---

description: "Tarefas de implementação — pick-and-place"
---

# Tasks: Pick-and-place

**Input**: documentos de desenho em `/specs/002-pick-and-place/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/pickplace-io.md](contracts/pickplace-io.md)

**Tests**: incluídos e **obrigatórios**. Não por preferência de estilo: o Artigo
V da constitution exige que todo comportamento de simulação tenha teste headless,
e o Artigo I.4 exige prova automatizada de determinismo.

**Organization**: agrupadas por história de usuário, cada uma entregável e
testável sozinha (Artigo VIII).

## Format: `[ID] [P?] [Story] Descrição`

- **[P]**: pode rodar em paralelo (arquivo diferente, sem dependência pendente)
- **[Story]**: a qual história pertence (US1…US4)

---

## Phase 1: Setup

**Purpose**: nada a inicializar — a feature vive dentro da estrutura existente.

Sem tarefas. Nenhum projeto novo, nenhuma dependência nova no `.csproj`. Isso é
resultado da decisão de dispositivo composto, e vale como confirmação de que ela
valeu a pena.

---

## Phase 2: Foundational (bloqueia todas as histórias)

**Purpose**: a capacidade de física que não existe hoje. **Se T002 falhar, o
desenho muda e nada mais desta lista vale** — por isso vem antes de tudo e tem
cenário headless como aceite, não inspeção visual.

- [x] T001 Adicionar `void SetKind(BodyKind kind)` a `IPhysicsBody` em `src/Forja.Core/Physics/IPhysicsWorld.cs`, com XML doc explicando que só a garra deve usar
- [x] T002 Implementar `SetKind` em `GodotPhysicsWorld.GodotBody` (`src/Forja.Core/Physics/GodotPhysicsWorld.cs`) via `PhysicsServer3D.BodySetMode`, preservando shape, space e entity id
- [x] T003 [P] Implementar `SetKind` na física falsa dos testes de lógica em `tests/Forja.Core.Tests/` (localizar o fake existente e estender)
- [x] T004 Criar `PickPlaceSpikeScenario` em `src/Forja.Studio/Headless/DeviceScenarios.cs`: peça rígida → cinemática → reposicionada por N ticks → rígida de novo → cai; assertar massa e atrito preservados e a peça em repouso após soltar
- [x] T005 Registrar `PickPlaceSpikeScenario` na fila de `src/Forja.Studio/Headless/HeadlessHost.cs`
- [x] T006 Rodar `godot --headless --path . -- --forja-tests` e confirmar V-A do [quickstart](quickstart.md). **Gate**: se reprovar, parar e reabrir R1 no [research.md](research.md) antes de qualquer outra tarefa

**Checkpoint**: a física sabe converter corpo em tempo de execução, provado sem GPU.

---

## Phase 3: US1 — Pegar, carregar e soltar (P1) 🎯 MVP

**Story Goal**: uma unidade pick-and-place operável pela tabela de I/O em modo
manual pega uma peça e a deposita em outro lugar.

**Independent Test**: V-B do [quickstart](quickstart.md) — forçar as coils na
ordem e ver a peça ser transportada. Inclui o caso negativo obrigatório: garra
no vazio não prende e não trava.

- [x] T007 [US1] Criar `src/Forja.Core/Devices/PickPlace.cs` com os campos de estado de [data-model.md](data-model.md) e o corpo cinemático do cabeçote em `Build`
- [x] T008 [US1] Implementar movimento dos dois eixos em `PickPlace.Tick`, reusando o padrão de rampa de `Piston.Tick` (clamp por velocidade × `SimContext.Dt`)
- [x] T009 [US1] Implementar agarrar: `QueryBox` no alcance da garra, filtrar só peças, escolher **menor Id** (R3), converter para `Kinematic` e guardar `heldPartId`
- [x] T010 [US1] Implementar conduzir: a cada tick, pose da peça presa := pose do cabeçote
- [x] T011 [US1] Implementar soltar: converter de volta para `Rigid`, chamar `Wake()` (R5) e limpar `heldPartId`
- [x] T012 [US1] Implementar `Teardown` desfazendo o vínculo (FR-009), e tratar peça removida durante o transporte sem deixar id órfão
- [x] T013 [US1] Implementar `WriteState` com as duas extensões e `heldPartId` na ordem fixa de [data-model.md](data-model.md), peça ausente como `0`
- [x] T014 [US1] Registrar `factory.Register("pick-place", () => new PickPlace())` em `src/Forja.Core/Devices/DeviceFactory.cs`
- [x] T015 [P] [US1] Criar `catalog/devices/actuator.pickplace.json` exatamente como o [contrato](contracts/pickplace-io.md#definição-de-catálogo)
- [x] T016 [P] [US1] Testes xUnit em `tests/Forja.Core.Tests/` com física falsa: agarra a de menor id entre várias; garra no vazio não prende; soltar devolve a rígido; `Teardown` desfaz vínculo
- [x] T017 [US1] Criar `PickPlaceScenario` headless: ciclo completo pegar → mover → soltar, assertando posição final da peça
- [x] T018 [US1] Registrar `PickPlaceScenario` em `HeadlessHost`
- [x] T019 [US1] Criar cena mínima de aceite manual em `plc/06-pick-and-place/pick-and-place.forja` (esteira, unidade, destino, sem programa ainda)
- [x] T020 [US1] Executar V-B do quickstart no app e registrar o resultado

**Checkpoint**: equipamento funcional e operável à mão. **Já é entregável** — o
usuário consegue demonstrar pick-and-place sem CLP externo.

---

## Phase 4: US2 — Sequência sem cronômetro (P2)

**Story Goal**: os fins de curso permitem escrever a sequência por confirmação
física em vez de por tempo.

**Independent Test**: V-C do quickstart — durante o movimento, `advanced` e
`retracted` são **ambos** falsos.

- [x] T021 [US2] Publicar os quatro fins de curso em `PickPlace.Tick` com a tolerância de `Piston` (R7), garantindo que não são negação um do outro
- [x] T022 [US2] Publicar `holding` a partir de `heldPartId`
- [x] T023 [P] [US2] Teste xUnit: no meio do curso os dois fins de curso do eixo são falsos; nos extremos, exatamente um é verdadeiro
- [x] T024 [US2] Estender `PickPlaceScenario` para avançar passos **somente** por fim de curso, provando SC-001 sem nenhum temporizador
- [x] T025 [US2] Executar V-C do quickstart

**Checkpoint**: o contrato de I/O está completo e provado.

---

## Phase 5: US3 — Cenário 06 da biblioteca (P3)

**Story Goal**: o trio final da Fase 1 do ROADMAP.

**Independent Test**: V-D do quickstart — ciclo contínuo sem intervenção.

- [x] T026 [US3] Completar `plc/06-pick-and-place/pick-and-place.forja` com esteira de origem, destino e HMI, seguindo as lições de geometria dos cenários 03–05 (guias não cruzam feixe; calha e caçamba nas posições validadas no T054)
- [x] T027 [US3] Escrever `plc/06-pick-and-place/pick-and-place.st` com a sequência canônica do [contrato](contracts/pickplace-io.md#sequência-canônica) e os três intertravamentos
- [x] T028 [US3] Escrever `plc/06-pick-and-place/README.md` explicando máquina de estado por passos, por que cada intertravamento existe e o que acontece sem ele (SC-005)
- [x] T029 [P] [US3] Atualizar os índices em `plc/README.md` e no `README.md` da raiz
- [x] T030 [US3] Verificar a cena sob master Modbus emulado, medindo que a premissa física se sustenta (peça ao alcance no ponto de coleta), como feito nos cenários 04 e 05
- [x] T031 [US3] Executar V-D e V-F do quickstart (ciclo contínuo e 20 transferências sem perder peça)

**Checkpoint**: Fase 1 do ROADMAP completa, 6 de 6 cenários.

---

## Phase 6: US4 — Geometria própria (P4)

**Story Goal**: o equipamento parece um equipamento.

**Independent Test**: V-B observado visualmente; a garra acompanha os dois eixos.

- [ ] T032 [US4] Modelar a unidade em `src/Forja.Studio/Rendering/DeviceVisuals.cs`: coluna, travessa, carro horizontal, haste vertical e garra, com nós nomeados para animação
- [ ] T033 [US4] Expor `ExtensionX`, `ExtensionY` e `HeldPartId` como leitura em `PickPlace` (Artigo II.2 — a camada visual só lê)
- [ ] T034 [US4] Animar os dois eixos em `SceneView.UpdateDeviceVisualState`, usando as mesmas fórmulas do `Tick`
- [ ] T035 [US4] Definir a caixa lógica do tipo em `SceneView.VisualParams` para o pick do editor bater com o equipamento

**Checkpoint**: coerente com os outros 17 tipos, já modelados.

---

## Phase 7: Polish & Cross-Cutting

- [ ] T036 [P] Executar V-E (determinismo) e confirmar que o cenário existente continua verde com a unidade em operação
- [ ] T037 [P] Executar V-G (orçamento de tick) e comparar a folga com a medição atual
- [ ] T038 Executar V-H (voltar para Edit com peça presa) e V-I (falha de driver com peça presa)
- [x] T039 Atualizar a contagem de dispositivos de 17 para 18 no `README.md` da raiz e onde mais aparecer
- [ ] T040 Rodar a suíte completa (`dotnet test` + `--forja-tests`) e confirmar CI verde no repositório público

---

## Dependencies

```
Phase 2 (T001–T006)  ── bloqueia tudo
        │
        ▼
Phase 3 US1 (T007–T020)  ── MVP entregável
        │
        ├──────────────▶ Phase 6 US4 (T032–T035)   visual, independente de US2/US3
        ▼
Phase 4 US2 (T021–T025)
        │
        ▼
Phase 5 US3 (T026–T031)
        │
        ▼
Phase 7 Polish (T036–T040)
```

US4 (visual) depende só de US1 e pode ser feita em paralelo com US2/US3 — mas é
P4 de propósito: não bloqueia nada e é a primeira coisa a cortar se o escopo
apertar.

## Parallel Opportunities

- **T003** com T002 (arquivos diferentes: fake dos testes vs `GodotPhysicsWorld`)
- **T015** e **T016** com o miolo de US1 (catálogo e testes vs comportamento)
- **T023** com T024
- **T029** com T026–T028
- **T036** e **T037** entre si

## Implementation Strategy

**MVP = Phase 2 + Phase 3.** Ao fim da US1 existe um pick-and-place funcionando,
operável à mão, demonstrável em vídeo. Tudo depois disso é enriquecimento.

**Gate real**: T006. É a única tarefa cujo fracasso invalida o plano inteiro, e
por isso é a sexta tarefa e não a sexagésima. Se `BodySetMode` em runtime não
preservar massa e atrito, o caminho vira recriar o corpo — com o custo de id que
o [ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md)
documenta — e as tarefas T007 em diante mudam de forma.

**Ordem de corte se o escopo apertar**: US4 primeiro (visual), depois US3
(o trio da biblioteca pode ficar para outra sessão sem perder o que foi feito).
US1 e US2 juntas são o mínimo que justifica ter aberto a v2.
