# Contrato — Canal de palavras no IoSnapshot

Estende o contrato `IPlcDriver` (contracts da spec 001/002) para transportar
palavras de 16 bits ao lado dos bits. Regras **W1–W6**. Toda implementação de
`IPlcDriver` em `Forja.Bellows` obedece.

## W1 — Formato

`IoSnapshot` carrega `ReadOnlyMemory<ushort> Words` paralelo a
`ReadOnlyMemory<bool> Bits`, ambos indexados por offset a partir de 0. O
comprimento de `Words` é `maiorOffsetDeRegistrador + 1` na direção
correspondente; offsets sem tag lêem 0.

## W2 — Simetria de direção

- **Snapshot de entrada** (core → driver, via `Exchange(inputs)`):
  `Bits` = discrete inputs, `Words` = input registers.
- **Snapshot de saída** (driver → core, retorno de `Exchange`):
  `Bits` = coils, `Words` = holding registers.

Um único formato serve às duas direções. `Exchange` continua **uma** troca.

## W3 — Bruto no fio

Todo valor em `Words` é contagem bruta `ushort` (0..65535). A conversão para
unidade de engenharia acontece **fora** do contrato, na `IoTable` (ver
`scaling-eu-raw.md`). Nenhuma implementação de driver interpreta a escala.

## W4 — Falha (herda C1)

Quando o driver falha, retorna `IoSnapshot` com `Valid=false`; a `IoTable`
ignora **tanto** `Bits` quanto `Words` desse snapshot (`ApplyOutputSnapshot`
já curto-circuita em `!Valid`). Nunca aplica setpoint velho (Artigo VII.1).

## W5 — Sem alocação por tick proporcional a pontos

O caminho de `Words` no `Exchange` e no `MirrorDataStore` reusa buffers
(double-buffer nos input registers; `ushort[]` fixo nos holding registers),
como os bits já fazem. Publicar/copiar palavras não aloca por tick em regime.

## W6 — Determinismo

As palavras publicadas por um mesmo estado de simulação são idênticas bit a bit
entre execuções (mesmo seed + mesmas entradas). A `IoTable` hasheia `_inputWords`
e `_outputWords` em ordem de endereço (Artigo I.4).

## Verificação

- Teste headless com driver simulado: publica palavras conhecidas, roda N ticks,
  confere que o `Words` recebido bate e que o hash é idêntico em duas execuções.
- `MirrorDataStore`: master lê input register e recebe o bruto publicado;
  master escreve holding register e o tick seguinte enxerga o valor (defasagem
  ≤ 1 tick — AS3/US2).
