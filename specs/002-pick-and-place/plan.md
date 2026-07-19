# Implementation Plan: Pick-and-place

**Branch**: `002-pick-and-place` | **Date**: 2026-07-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-pick-and-place/spec.md`

## Summary

A Forja ganha a capacidade de **prender** uma peça, que hoje não existe: ela
sabe empurrar, segurar e apagar. Sem isso, sequenciamento por passos com
intertravamento entre eixos — a classe de lógica mais citada em vaga de
automação — é inescrevível na bancada.

Abordagem, decidida no [ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md):
um **dispositivo composto** (`actuator.pickplace`) com dois eixos e garra,
apoiado numa ampliação mínima da abstração de física (`SetKind`) que permite a
peça alternar entre corpo rígido e conduzido. Sem mudança de `schemaVersion`,
sem hierarquia de dispositivos, sem tipo novo de peça.

## Technical Context

**Language/Version**: C# 12 / .NET 8

**Primary Dependencies**: Godot 4.4.1 (mono) · Jolt Physics via
`PhysicsServer3D` · NModbus (camada 3, não tocada aqui)

**Storage**: cena `.forja` (JSON, `schemaVersion` 1 — **inalterado**) e catálogo
de dispositivos em `catalog/devices/*.json`

**Testing**: xUnit (camadas 1–3, física falsa) + cenários headless em
`godot --headless -- --forja-tests` (física Jolt real, sem GPU)

**Target Platform**: Windows 10/11 x64

**Project Type**: aplicação desktop de simulação, quatro camadas

**Performance Goals**: 60 Hz fixo; orçamento de 16,6 ms por tick mantido com a
mesma folga atual (hoje p95 ≈ 4 ms com 200 peças)

**Constraints**: determinismo tick a tick verificado por hash (Artigo I.4);
headless obrigatório (Artigo V); sem GPU

**Scale/Scope**: 1 comportamento novo, 1 tipo de catálogo (18º), 1 método novo
na abstração de física, 1 cenário headless, 1 trio da biblioteca `plc/`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Artigo | Avaliação |
|---|---|
| **I — Determinismo** | ⚠️ **Exige atenção ativa.** Escolher *qual* peça agarrar é uma decisão nova, e `QueryBox` não garante ordem. Resolvido em research (R3): critério por menor `Id`, que é estável por construção. Extensões e id da peça presa entram no hash (FR-008). |
| **II — Camadas** | ✅ `SetKind` amplia a abstração de física da camada 2 em termos de domínio (`BodyKind`), sem citar Godot. Comportamento em `Forja.Core.Devices`, visual em `Forja.Studio`. Nenhuma dependência para cima. |
| **III — Cena é dado** | ✅ com ressalva avaliada, abaixo. |
| **IV — Driver é plugin** | ✅ Não tocado. As portas novas são endereços Modbus como quaisquer outros. |
| **V — Testabilidade headless** | ✅ Cenário headless é entregável obrigatório, e é o **aceite da primeira fatia**, não uma checagem final. |
| **VI — Contrato de I/O** | ✅ com ressalva avaliada, abaixo. |
| **VII — Falha segura** | ⚠️ Caso novo: o que acontece com a peça presa quando o driver cai e a simulação pausa. Resolvido em research (R4). |
| **VIII — Incrementos verticais** | ✅ As quatro histórias da spec são fatias verticais; a P1 é aceitável pelo modo manual, sem CLP externo. |

### Ressalva ao Artigo III.2 — avaliada, sem violação

> *"Adicionar um novo tipo de dispositivo não pode exigir recompilar o editor.
> Registro de dispositivos é feito via catálogo de dados + factory."*

Pick-and-place **exige código novo**: um comportamento registrado na
`DeviceFactory`. Isso não viola o artigo, e a distinção importa:

- Tipo novo que **reusa** comportamento existente é dado puro. É como
  `actuator.pusher` e `actuator.piston` coexistem hoje: dois tipos, um
  comportamento `piston`.
- **Máquina de natureza nova** exige comportamento novo. O próprio artigo prevê
  isso ao citar "catálogo de dados **+ factory**" — factory é código.

O que o artigo proíbe é recompilar **o editor**, e não é o caso: o editor lê o
catálogo e continua intocado.

### Ressalva ao Artigo VI.1 — avaliada, sem violação

> *"Todo sensor expõe exatamente um endereço de entrada. Todo atuador consome
> exatamente um endereço de saída."*

Um pick-and-place consome **três** saídas e expõe **cinco** entradas. Tensão
literal, mas a unidade de análise do artigo é a **porta**, não o dispositivo —
como já demonstra o `Piston` atual, que tem `extend` (Out) e `extended` (In).

O que o artigo proíbe é **mapeamento implícito**, e o desenho preserva isso
integralmente: cada uma das oito portas é declarada no catálogo e mapeada
explicitamente a um endereço no `ioMap` da cena. Zero inferência.

Nenhuma emenda à constitution é necessária.

## Project Structure

### Documentation (this feature)

```text
specs/002-pick-and-place/
├── spec.md              # o quê e por quê
├── plan.md              # este arquivo
├── research.md          # decisões técnicas com risco
├── data-model.md        # entidades e transições de estado
├── quickstart.md        # como provar que funciona
├── contracts/
│   └── pickplace-io.md  # contrato de portas e catálogo
├── checklists/
│   └── requirements.md  # qualidade da spec
└── tasks.md             # gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Forja.Anvil/                    # camada 1 — NÃO TOCADA
│   └── Catalog/                    # DeviceTypeDef já comporta as portas novas
├── Forja.Core/                     # camada 2
│   ├── Physics/
│   │   ├── IPhysicsWorld.cs        # + IPhysicsBody.SetKind
│   │   └── GodotPhysicsWorld.cs    # + implementação via BodySetMode
│   └── Devices/
│       ├── PickPlace.cs            # NOVO — comportamento
│       └── DeviceFactory.cs        # + registro de "pick-place"
├── Forja.Bellows/                  # camada 3 — NÃO TOCADA
└── Forja.Studio/                   # camada 4
    ├── Rendering/DeviceVisuals.cs  # + geometria do equipamento
    ├── Rendering/SceneView.cs      # + animação dos dois eixos e da garra
    └── Headless/DeviceScenarios.cs # + PickPlaceScenario

catalog/devices/
└── actuator.pickplace.json         # NOVO — 18º tipo

tests/
└── Forja.Core.Tests/               # física falsa: agarrar, soltar, hash

plc/06-pick-and-place/              # o trio final da Fase 1
├── pick-and-place.forja
├── pick-and-place.st
└── README.md
```

**Structure Decision**: estrutura existente de quatro camadas, sem projeto novo.
A feature toca três das quatro camadas e nenhuma dependência nova entra no
`.csproj`. A camada 1 (`Forja.Anvil`) fica intocada de propósito — é a prova de
que a decisão de dispositivo composto evitou mexer no schema.

## Complexity Tracking

> Preenchido apenas quando o Constitution Check tem violações a justificar.

Nenhuma violação. As duas ressalvas (III.2 e VI.1) foram avaliadas acima e
concluíram por **conformidade**, não por exceção justificada — por isso não
entram aqui.

A complexidade que **foi** deliberadamente evitada, e que valeria esta tabela se
tivesse sido aceita:

| Alternativa evitada | O que teria custado |
|---|---|
| `parentId` no `DeviceInstance` | `schemaVersion` 2, migração, hierarquia no editor, e um conceito novo em cena para todos os tipos |
| Recriar a peça ao agarrar | id novo por `SpawnBox`, quebrando o hash de estado de um jeito difícil de enxergar |
| Junta física entre garra e peça | expor juntas na abstração, e um comportamento menos determinístico |
