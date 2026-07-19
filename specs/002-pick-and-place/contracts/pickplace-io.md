# Contrato de I/O — `actuator.pickplace`

O que um programa de CLP enxerga da unidade pick-and-place. Este é o contrato
que a spec 002 congela: mudá-lo depois quebra programas escritos contra ele.

## Portas

Oito portas, cada uma mapeada explicitamente a um endereço no `ioMap` da cena
(Artigo VI.1 — nenhuma inferência).

### Saídas (o CLP comanda)

| Porta | Efeito |
|---|---|
| `advance` | `TRUE` estende o eixo horizontal até `strokeX`; `FALSE` recolhe a 0 |
| `lower` | `TRUE` desce o eixo vertical até `strokeY`; `FALSE` sobe a 0 |
| `grip` | `TRUE` aciona a garra; `FALSE` solta |

**Nível, não borda.** Manter a saída é manter o comando. Desligar `advance` no
meio do curso **recolhe** — não congela. Um pick-and-place não tem posição
intermediária comandável, e isso é fiel ao equipamento pneumático real, que só
tem os dois extremos.

### Entradas (o CLP lê)

| Porta | Significado |
|---|---|
| `advanced` | eixo horizontal no fim de curso avançado |
| `retracted` | eixo horizontal recolhido |
| `lowered` | eixo vertical embaixo |
| `raised` | eixo vertical no alto |
| `holding` | há peça presa na garra |

**`advanced` e `retracted` não são um a negação do outro.** No meio do curso os
dois são `FALSE`, e é assim que o programa sabe que o eixo está em movimento.
Escrever `NOT advanced` no lugar de `retracted` é o erro que este contrato
existe para tornar evidente — e é o mesmo raciocínio de intertravar contra
realimentação física do [cenário 02](../../../plc/02-intertravamento-emergencia/).

## Definição de catálogo

```json
{
  "typeId": "actuator.pickplace",
  "category": "Actuator",
  "displayName": "Pick-and-place (2 eixos)",
  "behavior": "pick-place",
  "visualScene": "",
  "ports": [
    { "portName": "advance",   "direction": "Out" },
    { "portName": "lower",     "direction": "Out" },
    { "portName": "grip",      "direction": "Out" },
    { "portName": "advanced",  "direction": "In"  },
    { "portName": "retracted", "direction": "In"  },
    { "portName": "lowered",   "direction": "In"  },
    { "portName": "raised",    "direction": "In"  },
    { "portName": "holding",   "direction": "In"  }
  ],
  "paramDefs": [
    { "name": "strokeX",   "type": "float", "default": 0.8,  "min": 0.1,  "max": 3 },
    { "name": "strokeY",   "type": "float", "default": 0.4,  "min": 0.05, "max": 2 },
    { "name": "speedX",    "type": "float", "default": 0.8,  "min": 0.1,  "max": 5 },
    { "name": "speedY",    "type": "float", "default": 0.6,  "min": 0.1,  "max": 5 },
    { "name": "gripRange", "type": "float", "default": 0.12, "min": 0.02, "max": 0.5 }
  ]
}
```

## Sequência canônica

O SFC clássico que o cenário 06 vai implementar, escrito **só com fins de
curso** como condição de avanço de passo — nenhum temporizador de percurso
(SC-001):

| Passo | Comando | Avança quando |
|---|---|---|
| 1 | `lower` | `lowered` |
| 2 | `grip` | `holding` |
| 3 | `NOT lower` | `raised` |
| 4 | `advance` | `advanced` |
| 5 | `lower` | `lowered` |
| 6 | `NOT grip` | `NOT holding` |
| 7 | `NOT lower` | `raised` |
| 8 | `NOT advance` | `retracted` → volta ao passo 1 |

## Intertravamentos que o contrato torna escrevíveis

O motivo de os fins de curso existirem. Cada um é uma proibição que o programa
**pode** expressar porque a informação física está disponível:

- **Não avançar com o eixo embaixo.** `advance` só com `raised` — senão a garra
  varre lateralmente na altura da esteira e derruba o que estiver no caminho.
- **Não descer sem estar no lugar.** `lower` só com `advanced` ou `retracted` —
  descer no meio do percurso deposita peça fora de posição.
- **Não soltar fora do destino.** `NOT grip` só com `advanced AND lowered`.

Nenhum desses é imposto pelo dispositivo. O equipamento obedece ao que for
comandado, inclusive comando ruim — como no mundo real. Impedir é trabalho do
programa, e é exatamente o que o cenário 06 ensina.

## Compatibilidade

Contrato **novo**: nenhum programa existente é afetado. Os cinco cenários já
publicados não usam este tipo e continuam válidos sem alteração.
