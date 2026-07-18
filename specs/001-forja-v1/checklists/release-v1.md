# Release v1 — validação do quickstart (T060)

Percurso V-A…V-G do [quickstart](../quickstart.md) contra o produto final.

- **Data:** 2026-07-18
- **Máquina:** Windows 11 x64 · Godot 4.4.1 .NET · .NET SDK 8.0.423
- **Commit:** ver `git log` a partir de `4ebdf14`
- **Veredicto:** [x] aprovado · [ ] reprovado

## Suíte automatizada

| Suíte | Comando | Resultado |
|---|---|---|
| Lógica (xUnit) | `dotnet test Forja.Studio.sln` | **111 testes, 0 falhas** (Anvil 17 · Core 76 · Bellows 15 · Arquitetura 3) |
| Física real (headless) | `godot --headless --path . -- --forja-tests` | **15 cenários, todos PASS** (114 s) |

## Cenários de validação

### V-A — Determinismo (RNF-03) ✅

`DeterminismScenario`: mesma cena, mesma semente e mesmo script de inputs em
duas execuções ⇒ hashes de estado idênticos.

### V-B — Física básica (RF-04) ✅

`ConveyorFlowScenario` (peça percorre a esteira e é removida **no sink**, não
na kill-zone), `ConveyorIoScenario` (esteira desligada não move a peça; ligada,
move), `ActuatorsScenario` (pistão empurra sem atravessar).

### V-C — Modo manual, sem PLC (RF-07 + RF-01 + RF-05) ✅

Automatizado: `SensorActuatorScenario` e `HmiScenario` (forçar bit pela
`ForceIoCommand`, driver nulo); `SimModeE2EScenario` prova que Pause congela o
tick, Step avança **exatamente 1** e Pause→Run retoma **sem salto**.

Manual: exercitado com o usuário na janela — Tabela de I/O com notação dupla,
forçar bit no ciclo Livre/1/0, e os botões de modo. Ver observação de UI abaixo.

### V-D — Endereço duplicado (RF-05, Artigo VI.3) ✅

`IoMapValidatorTests.EnderecoDuplicado_ErroCitaOsDoisDispositivos` — regra V1
do data-model. **Este aceite não tinha teste até o T060**: o validador já
implementava a regra, mas ninguém cobrava que a mensagem citasse os dois
dispositivos. Agora cobra (e o `SimulationLoop` bloqueia o Edit→Run com
qualquer erro de validação, com o diálogo ligado no `ValidationFailed`).

### V-E — OpenPLC ponta a ponta (RF-06, RF-09) ✅

Executado ao vivo com **OpenPLC v4** (Editor + Runtime 4.1.8) em 2026-07-18:
conexão, separação por altura comandada pelo PLC real e falha segura.
Resultado detalhado: [openplc-acceptance.md](./openplc-acceptance.md) — 10/10.

### V-F — Persistência round-trip (RF-08) ✅

`SceneSerializerTests` (round-trip `load(save(doc)) == doc` por igualdade
estrutural canônica) e `UndoRedoStack` sobre o mesmo mecanismo. Salvar/abrir
pela UI foi exercitado no aceite do editor.

### V-G — Editor do zero (RF-02) ✅

Aceite manual do usuário em 2026-07-17: montar a planta pelo editor sem tocar
em JSON. Resultado: [editor-acceptance.md](./editor-acceptance.md) — aprovado.

## RNFs medidos

| RNF | Limite | Medido | Onde |
|---|---|---|---|
| RNF-01 tick com 200 peças | < 16,6 ms | **p50 0,38 ms · p95 4,10 ms** | `PerfScenario` |
| RNF-03 determinismo | hash idêntico | idêntico | `DeterminismScenario` |
| RNF-04 startup | < 5 s | **1248 ms** headless · **1455 ms** com janela | `StartupLoadScenario` |
| RNF-05 cena 100 dispositivos | < 2 s | **12 ms** (parse 11 + montagem 1) | `StartupLoadScenario` |
| RNF-06 sem .NET instalado | roda | **roda** | pacote com `DOTNET_ROOT` inválido |
| RNF-07 estabilidade | memória ≤ 105% | **gerenciada 96,7% · nativa 101,9%** | `SoakScenario` |

RNF-06 foi verificado sem máquina limpa disponível: o pacote extraído rodou a
suíte headless inteira e abriu a demo com `DOTNET_ROOT` apontando para caminho
inexistente, `PATH` reduzido ao Windows e **apenas .NET 6.0.11 no sistema** —
o app precisa do 8, logo usou o runtime que veio no ZIP.

## Pendências assumidas para o release

1. **Soak manual de 8 h** (RNF-07) não foi executado — o proxy automatizado
   cobre 30 min simulados por rodada. Fica como passo de pré-release.
2. **Teste em máquina limpa de verdade** (RNF-06): a evidência acima é forte,
   mas não substitui um Windows sem .NET nenhum.
3. **Piso e dispositivos no editor**: durante o T054 o usuário relatou que o
   piso colocado depois ainda parece "engolir" parte da estrutura já montada,
   mesmo após o `flushToGround` (commit cd30da5). Não bloqueia — é visual e o
   `.forja` sai correto — mas fica registrado para a v1.1.

## Observação de UI encontrada no T054 (corrigida)

O aceite com PLC real revelou que **quatro painéis (HMI, Tabela de I/O,
Conexão PLC e Propriedades) estavam sendo renderizados fora da janela** e que
os botões da barra de modos não recebiam clique (um contêiner invisível os
cobria). Corrigido no commit `9c70322` e revalidado ao vivo com o PLC
conectado. Os cenários headless não pegavam isso: eles não instanciam UI.
