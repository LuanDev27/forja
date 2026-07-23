# Contrato — Escalonamento EU ↔ bruto e validação analógica

Onde e como a unidade de engenharia vira contagem bruta (e volta), e quais
mapeamentos analógicos a validação aceita. Regras **S1–S4** (escala) e
**V1–V3** (validação).

## S1 — Fronteira única

A conversão vive só na `IoTable` (camada 2). O dispositivo fala **EU** (`float`);
o registrador carrega **bruto** (`ushort`); o programa de CLP reescala em ST. O
`float` nunca cruza o contrato `IPlcDriver` (ver `iosnapshot-words.md` W3).

## S2 — Fórmula (linear, saturante)

Dado o ponto com faixa de engenharia `[euMin, euMax]` (do tipo) e faixa bruta
`[rawMin, rawMax]` (do cartão na cena):

**Entrada (device EU → registrador bruto):**

```
t   = (eu − euMin) / (euMax − euMin)
raw = round( rawMin + t·(rawMax − rawMin), ToEven )
raw = clamp(raw, min(rawMin,rawMax), max(rawMin,rawMax))
```

**Saída (registrador bruto → device EU):**

```
t  = (raw − rawMin) / (rawMax − rawMin)
eu = euMin + t·(euMax − euMin)
```

## S3 — Saturação, não estouro

EU fora de `[euMin, euMax]` satura no limite bruto correspondente — nunca
estoura o `ushort`, nunca dá wrap-around, nunca lança (edge case da spec).

## S4 — Determinismo do arredondamento

`round` usa `MidpointRounding.ToEven` fixo. A mesma EU sempre produz a mesma
contagem bruta → o hash (W6) é estável. Documentado porque é o modo de falha
silenciosa que o Artigo I existe para pegar.

## V1 — Matriz direção × área × tipo

Substitui o antigo `analog-not-supported`. `IoMapValidator` aceita:

| PortType | Direction | Servidor (PLC é master) | Cliente (Forja é master) |
|---|---|---|---|
| Bool | In  | DiscreteInput   | DiscreteInput, Coil |
| Bool | Out | Coil            | Coil |
| Word | In  | InputRegister   | InputRegister, HoldingRegister |
| Word | Out | HoldingRegister | HoldingRegister |

Fora da matriz → erro `type-area-mismatch` (bloqueia Edit→Run, Artigo VI.3).

## V2 — Escala válida

`AnalogScale` com `RawMin == RawMax`, ou tipo com `euMin == euMax`, é erro
`invalid-scale` — pego na validação, nunca vira divisão por zero em runtime.

## V3 — Conflito de endereço com palavra

`duplicate-address` já cobre palavras: `IoAddress` inclui a área, então `%IW0`
(InputRegister 0) e `%QX0.0` (Coil 0) não colidem, mas dois `%IW0` sim. Erro,
não warning (Artigo VI.3, FR-013).

## Verificação

- Tabela de casos de escala: `(euMin,euMax,rawMin,rawMax,eu) → raw` esperado,
  incluindo meio de escala, fundo, topo, e EU fora da faixa (satura).
- Round-trip: `raw → eu → raw` estável dentro da resolução de 16 bits.
- Validação: um caso por célula inválida da matriz + `invalid-scale` +
  `duplicate-address` com dois `%IW0`.
