# Data Model — Pick-and-place

Nenhuma mudança em `schemaVersion`. O formato `.forja` continua na versão 1, e
cenas antigas seguem carregando. Isso é consequência direta da decisão de
dispositivo composto ([ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md)).

## Entidades

### Unidade pick-and-place

Equipamento único com dois eixos e uma garra. Instanciado como qualquer outro
dispositivo: `typeId`, `transform`, `params`.

**Parâmetros** (todos com default no catálogo, todos opcionais na cena):

| Parâmetro | Tipo | Default | Papel |
|---|---|---|---|
| `strokeX` | float | 0,8 | curso do eixo horizontal, em metros |
| `strokeY` | float | 0,4 | curso do eixo vertical, em metros |
| `speedX` | float | 0,8 | velocidade do eixo horizontal, m/s |
| `speedY` | float | 0,6 | velocidade do eixo vertical, m/s |
| `gripRange` | float | 0,12 | raio de alcance da garra, em metros |

O eixo horizontal avança ao longo do **+X local** (mesma convenção do `Piston`,
então `rotY` da cena orienta a unidade). O vertical desce em **−Y**.

**Estado interno** (não persistido — reconstruído a cada entrada em Run):

| Campo | Tipo | Papel |
|---|---|---|
| `extensionX` | float | 0..`strokeX`, posição corrente do eixo horizontal |
| `extensionY` | float | 0..`strokeY`, quanto o eixo vertical desceu |
| `heldPartId` | uint? | peça presa, ou ausente |

Repouso é `extensionX = 0`, `extensionY = 0` — recolhido e no alto.

### Vínculo garra-peça

Relação temporária entre a unidade e **uma** peça. Não é entidade persistida:
existe apenas em memória enquanto a simulação roda.

**Ciclo de vida**:

```
  (sem vínculo)
       │
       │  garra acionada E existe peça ao alcance
       ▼
  peça vira Kinematic, id guardado em heldPartId
       │
       │  a cada tick: pose da peça := pose do cabeçote
       │
       ├── garra desacionada ──────┐
       └── saída de Run (Teardown) ┤
                                   ▼
            peça volta a Rigid, é acordada, vínculo desfeito
```

**Invariantes**:

1. No máximo **um** vínculo por unidade.
2. Uma peça não pode estar presa a duas unidades — garantido porque uma peça
   já cinemática não é candidata (só peças `Rigid` são elegíveis).
3. `heldPartId` sempre referencia peça existente. Se a peça for removida
   (entrou numa saída, saiu da kill-zone), o vínculo se desfaz sozinho.

## Transições de estado dos eixos

Cada eixo é independente e segue o padrão já usado por `Piston`:

```
alvo := comando ? curso : 0
extensão := clamp(extensão + sinal(alvo − extensão) × velocidade × Δt, 0, curso)
```

Consequência importante para quem escreve o CLP: **desligar o comando recolhe**,
não congela. Um eixo parado no meio do curso só existe durante a transição.

## Contribuição ao hash de estado

Ordem fixa, sempre os três campos, sempre presentes:

| Ordem | Campo | Codificação |
|---|---|---|
| 1 | `extensionX` | `AddQuantized` (mesma escala do `Piston`) |
| 2 | `extensionY` | `AddQuantized` |
| 3 | `heldPartId` | `Add(uint)`, com **0** representando "nenhuma" |

Incluir o id da peça presa é o que faz o Artigo I.4 detectar a divergência mais
provável desta feature: duas execuções agarrando **peças diferentes** com poses
idênticas produziriam estados visualmente iguais e hashes diferentes — que é
exatamente o que se quer que o teste acuse.

## Peça

**Inalterada.** Nenhum campo novo, nenhum tipo novo. A única diferença é que o
`BodyKind` do corpo de uma peça deixa de ser imutável na prática — mas isso é
propriedade do corpo físico, não do dado da peça.
