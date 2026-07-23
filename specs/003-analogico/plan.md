# Implementation Plan: Sinais analógicos

**Branch**: `003-analogico` | **Date**: 2026-07-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-analogico/spec.md`

## Summary

Fiar **palavras de 16 bits por um cano que hoje só passa bits**. O contrato
`IoSnapshot` ganha um canal `Words` paralelo a `Bits`; a `IoTable` ganha buffers
`ushort[]` de entrada/saída, força numérica, conversão EU↔bruto na fronteira, e
hasheia palavras junto dos bits; o `MirrorDataStore` passa a fiar de verdade os
`RegisterSource` (double-buffer para input registers, cópia por tick dos holding
registers); `PortDef` ganha `PortType { Bool, Word }`; o schema sobe 1→2 com uma
migração **aditiva** registrada; a validação troca `analog-not-supported` por
uma regra direção×área×tipo; e nascem três dispositivos analógicos (sensor de
nível, balança, esteira de velocidade variável) com catálogo e cenários headless.

Abordagem em **incrementos verticais** (Artigo VIII), cada um uma fatia
demonstrável: a camada 1 (o contrato) é o gate — nada acima dela se escreve
antes de o cano de palavras existir e ter teste de determinismo verde.

## Technical Context

**Language/Version**: C# / .NET 8 (`net8.0`)

**Primary Dependencies**: NModbus (driver Modbus TCP), Godot 4 (camada 4, fora do
núcleo testável), `System.Text.Json` (persistência de cena)

**Storage**: arquivos `.forja` (JSON versionado por `schemaVersion`)

**Testing**: testes .NET headless (sem GPU, sem PLC — Artigo V); driver simulado
para o caminho sem PLC; OpenPLC v4 real para validação ponta a ponta manual

**Target Platform**: Windows 10/11 x64

**Project Type**: desktop-app / simulador determinístico de 4 camadas
(Studio · Core · Anvil · Bellows — ver constitution)

**Performance Goals**: tick fixo 60 Hz; o caminho de palavras no `Exchange` e no
`MirrorDataStore` **não aloca por tick** proporcional ao número de pontos
(double-buffer reutilizado, como os bits já fazem)

**Constraints**: determinismo (mesmo seed + mesmas entradas ⇒ mesmo hash,
Artigo I.4); palavra é `ushort` bruto no fio, `float` (EU) só na fronteira;
migração aditiva sem regressão dos 18 dispositivos digitais

**Scale/Scope**: 4 arquivos de camada 1/2 tocados no núcleo (`IPlcDriver`,
`IoTable`, `DeviceTypeDef`, `IoTypes`/`SceneDocument`), 2 no driver
(`MirrorDataStore`, modo cliente), 1 na validação, 1 na UI, + 3 dispositivos
novos com catálogo e cenários

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Artigo | Como esta fase cumpre |
|---|---|
| **I — Determinismo** | Palavras entram no hash em ordem de endereço (FR-014); `ushort` bruto é determinístico; conversão EU→bruto arredonda de forma fixa e documentada. Teste de hash idêntico cobre uma cena analógica. |
| **II — Camadas** | Palavra desce só: contrato (1) → IoTable (2) → driver (3). Núcleo roda headless; a UI (4) só exibe/força, nunca muta estado direto. |
| **III — Cena é dado** | Ponto analógico e faixa bruta são dados na cena; tipo novo entra por catálogo JSON sem recompilar o editor; `schemaVersion` 1→2 com migração aditiva registrada e falha explícita em campo malformado (FR-009, FR-010). |
| **IV — Driver é plugin** | Só `MirrorDataStore` e o modo cliente do Bellows mudam; o core continua conhecendo apenas `IPlcDriver`. Driver simulado cobre os testes. |
| **V — Headless** | Cada dispositivo novo vem com cenário "monta cena → roda N ticks → asserta" sem GPU e sem PLC (FR-018, SC-006). |
| **VI — I/O explícito** | Uma palavra = um endereço de registrador; conflito de endereço com palavra é erro de validação (FR-013); regra direção×área×tipo substitui `analog-not-supported` (FR-011, FR-012). |
| **VII — Falha segura** | `Valid=false` já pausa; malha analógica não aplica setpoint velho; campo v2 malformado falha com caminho+motivo. |
| **VIII — Incremento vertical** | Ordem P1 (ler `%IW`) → P2 (escrever `%QW`) → P3 (malha de nível), cada uma demonstrável sozinha; P2 do schema (US4) prova a carga aditiva. |

**Resultado do gate: PASSA.** Nenhuma violação a justificar — a mudança é
aditiva e mantém as invariantes de camada 1. Complexity Tracking vazio.

## Project Structure

### Documentation (this feature)

```text
specs/003-analogico/
├── spec.md              # Especificação (feita)
├── plan.md              # Este arquivo
├── research.md          # Fase 0 — decisões técnicas ancoradas no ADR 0005
├── data-model.md        # Fase 1 — entidades: palavra, cartão, faixa EU, PortType
├── quickstart.md        # Fase 1 — como validar as 4 US ponta a ponta
├── contracts/           # Fase 1 — contrato do canal de palavras e da conversão
│   ├── iosnapshot-words.md
│   └── scaling-eu-raw.md
├── checklists/
│   └── requirements.md  # Checklist de qualidade da spec (feito)
└── tasks.md             # /speckit-tasks — NÃO criado por /speckit-plan
```

### Source Code (repository root)

```text
src/
├── Forja.Anvil/                    # Camada 1 — Domínio (a bigorna)
│   ├── Contracts/IPlcDriver.cs     # IoSnapshot += ReadOnlyMemory<ushort> Words
│   ├── Catalog/DeviceTypeDef.cs    # PortDef += PortType { Bool, Word }; ParamDef já serve euMin/euMax
│   ├── Scene/IoTypes.cs            # IoTag/IoAddress já têm as áreas de registrador; scale opcional no ponto
│   ├── Scene/SceneDocument.cs      # CurrentSchemaVersion 1→2
│   └── Validation/IoMapValidator.cs# remove analog-not-supported; regra direção×área×tipo
├── Forja.Core/                     # Camada 2 — Simulação
│   ├── Io/IoTable.cs               # ushort[] in/out, força numérica, EU↔bruto, hash de palavras, IoPointView += valor numérico
│   ├── Persistence/SceneSerializer.cs # registra ISceneMigration 1→2 (aditiva)
│   └── Devices/                    # 3 comportamentos novos (nível, balança, esteira VV)
├── Forja.Bellows/                  # Camada 3 — IO
│   └── Modbus/MirrorDataStore.cs   # RegisterSource fiado: input regs (double-buffer) + holding regs (cópia por tick)
│       + ModbusTcp*Driver.cs       # Exchange passa Words nos dois sentidos
└── Forja.Studio/                   # Camada 4 — Apresentação
    └── UI/IoTablePanel.cs          # exibe número+unidade; força valor numérico

catalog/devices/                    # JSON dos 3 dispositivos novos (Artigo III.2)
tests/                              # cenários headless por dispositivo + teste de determinismo + teste de migração v1→v2
```

**Structure Decision**: mantém a arquitetura de 4 camadas já fixada na
constitution. A mudança é vertical e concentrada na camada 1 (o contrato) e sua
propagação para cima; nenhuma camada nova, nenhum projeto novo. O nome dos
módulos e namespaces segue a tabela da constitution.

## Complexity Tracking

> Sem violações da constitution — seção vazia intencionalmente.
