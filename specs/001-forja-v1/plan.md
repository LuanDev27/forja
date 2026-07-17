# Implementation Plan: Forja v1 — Simulador de Plantas Industriais

**Branch**: `001-forja-v1` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-forja-v1/spec.md`

## Summary

Simulador 3D de plantas industriais (estilo Factory I/O) para validação de
lógica de PLC sem hardware. O usuário monta a planta num editor 3D (catálogo de
esteiras, sensores, atuadores, HMI), roda a simulação física determinística a
60 Hz e conecta a um PLC real ou soft-PLC via Modbus TCP — ou opera em modo
manual sem PLC.

Abordagem técnica: Godot 4.4 (C#/.NET 8) com física Jolt nativa; quatro
assemblies em camadas (`Forja.Anvil` → `Forja.Core` → `Forja.Bellows` →
`Forja.Studio`) com dependências apenas para baixo, impostas por referência de
projeto; cena persistida em `.forja` (JSON versionado); driver de PLC como
plugin atrás de `IPlcDriver` (v1: Modbus TCP via NModbus + driver nulo);
testes headless em dois níveis (xUnit puro .NET + GdUnit4 em
`godot --headless`).

## Technical Context

**Language/Version**: C# 12 / .NET 8 · Godot 4.4.x .NET (versão pinada — ver research R1)

**Primary Dependencies**: GodotSharp (engine, física Jolt nativa), NModbus 3.x (Modbus TCP), System.Text.Json (persistência), xUnit (testes .NET), GdUnit4 (testes headless Godot)

**Storage**: arquivos `.forja` — JSON versionado (`schemaVersion`), legível e diffável. Sem banco de dados.

**Testing**: xUnit para Anvil/Core (lógica)/Bellows; GdUnit4 rodando em `godot --headless` para comportamento físico e integração de cena. CI: build + ambos os conjuntos.

**Target Platform**: Windows 10 21H2+ / Windows 11 x64. Distribuição: ZIP portátil, export Godot self-contained (sem .NET pré-instalado — RNF-06, decisão Q4 da spec).

**Project Type**: desktop-app (aplicação Godot única com bibliotecas .NET em camadas)

**Performance Goals**: ≥ 60 FPS com 200 peças ativas (RNF-01); tick fixo 60 Hz; latência sensor→registro Modbus < 20 ms (RNF-02); startup < 5 s (RNF-04); cena de 100 dispositivos < 2 s (RNF-05)

**Constraints**: determinismo — mesmo seed + mesmos inputs ⇒ hash idêntico em 10.000 ticks (RNF-03, Artigo I); 8 h em Run sem crescimento de memória > 5% (RNF-07); core roda em `godot --headless` (Artigo II)

**Scale/Scope**: catálogo v1 com 16 tipos de dispositivo (RF-03); cenas típicas de dezenas a ~100 dispositivos e até 200 peças ativas; 1 cena demo (separador por altura) com programa ladder OpenPLC incluído

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Artigo | Gate | Como o plano satisfaz |
|---|---|---|
| I — Determinismo | Fixed timestep, sem random sem seed, iteração estável, teste de hash | Toda lógica de simulação em `SimulationLoop.Tick()` chamado só de `_PhysicsProcess` (60 Hz, `physics/common/physics_ticks_per_second=60`). `IRandomSource` (Anvil) injetado, seedado pela cena. Entidades em lista ordenada por `EntityId` crescente (nunca `Dictionary` para iteração). Teste `DeterminismTest`: mesma cena + seed + script de inputs → `StateHasher` (FNV-1a sobre estado quantizado) idêntico em 10.000 ticks — criado na primeira fatia vertical. |
| II — Camadas | Dependências só para baixo; core sem UI; presentation não muda estado; core headless | Imposto por referência de csproj: `Anvil` (0 refs) ← `Core` ← `Bellows`, `Studio` (ref todos). Teste de arquitetura (NetArchTest) proíbe `Godot.Control`/`Godot.Input`/`CanvasItem` em `Core` e qualquer `Godot.*` em `Anvil`/`Bellows`. Studio emite `ISimCommand` numa fila; core consome no próximo tick. GdUnit4 roda tudo em `godot --headless`. |
| III — Cena é dado | JSON versionado; dispositivo novo sem recompilar editor; migração explícita | `.forja` com `schemaVersion` int + pipeline de migração (`ISceneMigration` encadeadas; versão desconhecida ⇒ erro com caminho e motivo). Catálogo de dispositivos em `catalog/devices/*.json` + `DeviceFactory` data-driven; o editor lista o catálogo em runtime. |
| IV — Driver é plugin | Core só conhece `IPlcDriver`; troca por config; driver nulo | `IPlcDriver` definido em `Forja.Anvil` (camada de contratos). `Forja.Bellows` implementa `ModbusTcpDriver` e `NullDriver` (modo manual, RF-07). Seleção de driver é campo da `ConnectionConfig` na cena. S7/EtherNet-IP futuros entram como novas classes em Bellows sem tocar Anvil/Core. |
| V — Testabilidade headless | Teste sem GPU e sem PLC; forma monta→ticks→asserta; CI vermelho não mergeia | Padrão de teste de dispositivo documentado em quickstart.md: montar `SceneDocument` mínimo → `SimulationLoop.Run(n)` → assert. `NullDriver` + master NModbus de loopback nos testes de Bellows. CI (GitHub Actions, runner Windows): `dotnet test` + `godot --headless` GdUnit4. |
| VI — Contrato de I/O | 1 sensor = 1 entrada, 1 atuador = 1 saída; mapa de tags é dado; conflito = erro | `IoTag` (device.port → endereço) serializado na cena, editável na Tabela de I/O (RF-05). `IoMapValidator` roda ao entrar em Run: endereço duplicado ⇒ `ValidationError` apontando os dois dispositivos, bloqueia Run. |
| VII — Falha segura | Perda de PLC pausa; timeout configurável; erro de cena com caminho e motivo | `IPlcDriver` reporta `DriverState`; transição para `Faulted` ⇒ core entra em Pause + evento para UI. `ConnectionConfig.TimeoutMs` (default 1000 ms). Loader de cena retorna `Result` com caminho/linha/motivo; proibido catch vazio (regra de análise no CI). |
| VIII — Incrementos verticais | Tarefa = fatia ponta a ponta com aceite | Repassado ao `/speckit-tasks`: cada tarefa entrega fatia demonstrável (ex.: T-01 "caixa cai no chão e hash é estável" atravessa Anvil+Core+Studio). Critérios de aceite da spec mapeados por tarefa. |

**Resultado do gate (pré-Phase 0):** PASS — nenhuma violação; nenhum item em Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-forja-v1/
├── plan.md              # Este arquivo
├── research.md          # Phase 0 — decisões técnicas (R1–R8)
├── data-model.md        # Phase 1 — entidades e regras de validação
├── quickstart.md        # Phase 1 — guia de validação/execução
├── contracts/
│   ├── iplcdriver.md    # Contrato do driver de PLC (Artigo IV)
│   ├── forja-schema.md  # Contrato do arquivo .forja (Artigo III)
│   └── modbus-mapping.md# Mapeamento de endereços Modbus (Artigo VI)
└── tasks.md             # Phase 2 — /speckit-tasks (não criado pelo plan)
```

### Source Code (repository root)

```text
project.godot                    # Projeto Godot (renderer forward_plus, Jolt, 60 Hz)
Forja.sln
src/
├── Forja.Anvil/                 # Camada 1 — Domain (net8.0, zero dependências)
│   ├── Forja.Anvil.csproj
│   ├── Contracts/               # IPlcDriver, IRandomSource, ISimCommand, Result
│   ├── Scene/                   # SceneDocument, DeviceInstance, IoTag, ConnectionConfig
│   ├── Catalog/                 # DeviceTypeDef, PortDef (schema do catálogo)
│   └── Validation/              # ValidationError, IoMapValidator (regras puras)
├── Forja.Core/                  # Camada 2 — Simulation (net8.0, ref: Anvil; GodotSharp sem UI)
│   ├── Forja.Core.csproj
│   ├── Loop/                    # SimulationLoop, SimMode (Edit/Run/Pause/Step), TickContext
│   ├── Devices/                 # Comportamentos: Conveyor, PhotoSensor, Piston, Emitter…
│   ├── Physics/                 # Integração Jolt via PhysicsServer3D (headless-safe)
│   ├── Io/                      # IoTable (snapshot de I/O por tick), troca com IPlcDriver
│   ├── State/                   # StateHasher (FNV-1a), SeededRandom : IRandomSource
│   └── Persistence/             # SceneSerializer, ISceneMigration + pipeline
├── Forja.Bellows/               # Camada 3 — IO (net8.0, ref: Anvil, Core; NModbus)
│   ├── Forja.Bellows.csproj
│   ├── Modbus/                  # ModbusTcpDriver (servidor Modbus TCP — ver R3)
│   └── Null/                    # NullDriver (modo manual / testes)
└── Forja.Studio/                # Camada 4 — Presentation (csproj do jogo Godot, ref: todos)
    ├── Editor/                  # Colocação, seleção, gizmos, snap, undo/redo (command pattern)
    ├── UI/                      # Toolbar de modos, Tabela de I/O, status de conexão, diálogos
    ├── Rendering/               # Nós visuais dos dispositivos (sincronizados do estado do core)
    └── Commands/                # ISimCommand emitidos para a fila do core

tests/
├── Forja.Anvil.Tests/           # xUnit — schema, validação, contratos
├── Forja.Core.Tests/            # xUnit — lógica sem física (IoTable, hasher, migrações, modos)
├── Forja.Bellows.Tests/         # xUnit — ModbusTcpDriver contra master NModbus loopback; NullDriver
├── Forja.Architecture.Tests/    # xUnit + NetArchTest — regras de camadas (Artigo II)
└── Forja.Headless.Tests/        # GdUnit4 (godot --headless) — física, dispositivos, determinismo E2E

catalog/devices/*.json           # Catálogo data-driven (Artigo III)
assets/devices/*.tscn            # Visual dos dispositivos (só apresentação)
demo/
├── separador-altura.forja       # Cena demo (RF-09)
└── openplc/separador.st         # Programa ladder/ST de exemplo
```

**Structure Decision**: aplicação Godot única com 4 assemblies em camadas +
5 projetos de teste, espelhando 1:1 os módulos da constitution
(Anvil/Core/Bellows/Studio). A direção de dependência é imposta pelo grafo de
referências da solution e verificada por teste de arquitetura — violação
quebra o build (Artigo II).

## Complexity Tracking

Sem violações da constitution — tabela vazia.

## Constitution Re-Check (pós-Phase 1)

Re-avaliado após gerar research.md, data-model.md e contracts/:

- **PASS.** Os contratos reforçam os artigos: `iplcdriver.md` (IV, VII),
  `forja-schema.md` (III, VI), `modbus-mapping.md` (VI). O data-model define
  ordem estável de entidades (I.3) e as transições de modo (RF-01) sem estado
  fora do core (II.2).
- Ponto de atenção registrado em research R3: o papel Modbus da v1 é
  **servidor** (PLC master faz polling), interpretação do RF-06 alinhada ao
  fluxo documentado do OpenPLC — flag para revisão do usuário; não é violação
  constitucional (driver continua plugin).
