# 03 — Contagem e batelada

Contar peças parece a coisa mais simples que um CLP faz. É onde moram alguns
dos erros mais teimosos da automação: contar duas vezes a mesma peça, zerar no
momento errado, e o clássico erro de um.

| Arquivo | O que é |
|---|---|
| `contagem.forja` | esteira, trava (*stopper*), sensor, 3 botoeiras, 2 sinaleiros |
| `contagem.st` | o programa |

Pré-requisito: o [cenário 01](../01-partida-parada-selo/) (selo de partida).

## O que a planta faz

A trava segura a fila. Com a máquina ligada ela abre, as peças escoam e o sensor
conta. Na quinta peça a batelada fecha: a trava sobe e represa o que vem atrás,
o sinaleiro amarelo acende. Alguém aperta RECONHECER, o contador zera, a trava
abre e começa a batelada seguinte.

## O contador CTU

`CTU` é o contador crescente padrão da IEC 61131-3. Cinco pinos:

| Pino | Papel |
|---|---|
| `CU` | conta +1 a cada **borda de subida** deste sinal |
| `R` | zera `CV` |
| `PV` | *preset value* — a meta |
| `CV` | *current value* — quanto já contou |
| `Q` | `TRUE` quando `CV >= PV` |

```pascal
counter(CU := part_seen AND running,
        R  := trig_ack.Q,
        PV := BATCH_SIZE);
```

**`CU` é sensível a borda por dentro do bloco.** É isso que permite ligar o
sensor direto. A peça passa ~240 ms na frente do feixe; com scan de 50 ms, isso
são cinco ciclos com o sinal em 1 — e mesmo assim conta **uma vez só**, porque o
bloco só reage à transição.

Se `CU` fosse por nível, uma peça viraria cinco. Esse é o erro que faz o
operador jurar que a máquina está contando errado.

Existem também `CTD` (decrescente, útil para "faltam N") e `CTUD` (os dois).

## O filtro de repique

```pascal
debounce(IN := sensor_raw, PT := T#20ms);
part_seen := debounce.Q;
```

O `CU` por borda resolve a peça que demora. Não resolve o **repique**: pulsos
curtos e espúrios que o sensor gera sozinho. Peça com furo no meio, superfície
brilhante refletindo, vibração da estrutura, respingo passando — cada um desses
produz um 1-0-1 rapidíssimo, e cada transição vira uma contagem.

O `TON` só deixa `part_seen` subir depois que o sinal ficou **estável por 20 ms**.
Pulso mais curto que isso morre no filtro.

A escolha do tempo é um cálculo, não um chute:

- **Piso:** maior que o repique esperado (alguns ms).
- **Teto:** menor que o tempo da peça no feixe. A 0,5 m/s, uma peça de 12 cm
  leva 240 ms cruzando. Um filtro de 300 ms perderia todas as peças.

20 ms fica confortavelmente no meio. Se a esteira acelerar, esse número precisa
ser revisto — é uma dependência entre o programa e a mecânica que não aparece
em lugar nenhum do código.

## Ordem de avaliação: onde este cenário morde

```pascal
trig_ack(CLK := btn_ack);     (* PRIMEIRO  *)

counter(CU := ..., R := trig_ack.Q, PV := BATCH_SIZE);   (* DEPOIS *)
```

Em ST as linhas rodam de cima para baixo dentro do ciclo. O `R_TRIG` do
reconhecimento **tem** que ser avaliado antes da chamada do contador.

Se estivesse depois, o pulso de reset só chegaria ao contador no ciclo seguinte.
Nesse ciclo intermediário `counter.Q` ainda estaria `TRUE`, a trava reabriria
por 50 ms e escaparia peça da batelada seguinte. Cinquenta milissegundos, uma
peça a mais na caixa, e uma tarde inteira procurando o motivo.

É o mesmo raciocínio do intertravamento no
[cenário 02](../02-intertravamento-emergencia/): em CLP, **onde** a linha está é
tão significativo quanto o que ela diz.

## O erro de um

`Q` sobe quando `CV >= PV`. Com `PV := 5`, `Q` vai a `TRUE` no instante em que a
**quinta** peça é contada — não depois dela, não na sexta.

Parece óbvio escrito assim. Na prática é a origem de metade dos bugs de batelada,
porque a pergunta "a quinta peça faz parte desta batelada ou da próxima?" tem
resposta diferente dependendo de **onde o sensor está** em relação ao ponto de
corte.

Aqui o sensor está **depois** da trava: quando `Q` sobe, as cinco peças já
passaram do ponto de bloqueio e seguem para o fim da esteira. A trava fecha
sobre a **sexta**. É por isso que a contagem fecha certa.

Mudar o sensor para antes da trava mudaria o significado sem mudar uma linha do
programa. Vale desenhar a planta antes de escrever a lógica.

## Constante nomeada, não número solto

```pascal
VAR CONSTANT
  BATCH_SIZE : INT := 5;
END_VAR
```

`BATCH_SIZE` é a receita — o número que muda quando o cliente muda o pedido.
Deixá-lo como `5` solto no meio da lógica significa caçar todas as ocorrências
na próxima mudança, e errar uma.

Num equipamento de verdade esse valor não seria nem constante: viria de uma
`%MW` escrita pelo supervisório, para o operador trocar a receita sem
recompilar. Como a Forja v1 é digital-only ([ADR 0001](../../adr/0001-visao-de-integracao-ecossistema.md),
observação O3), aqui ele é constante — e é exatamente esse limite que a Fase 2
do [ROADMAP](../../ROADMAP.md) vai remover.

## Retentividade

Pergunta que vale fazer em toda batelada: **se faltar energia com três peças
contadas, o que deve acontecer?**

Muitos CLPs permitem declarar o contador como retentivo — `CV` sobrevive ao
desligamento e a batelada continua de onde parou. Faz sentido se as três peças
continuam fisicamente na linha. Não faz nenhum se alguém esvaziou a esteira
durante a parada, e aí a caixa sai com três a menos.

Não existe resposta certa universal: depende de o material permanecer ou não.
O que não pode é a escolha ser acidental.

## Como rodar

1. Forja: **Abrir…** → `plc/03-contagem-batelada/contagem.forja`
2. OpenPLC v4: carregue `contagem.st`, Forja como dispositivo remoto Modbus TCP
   (passo a passo em [`demo/openplc/README.md`](../../demo/openplc/README.md)).
3. **Rodar** na Forja, Start no OpenPLC.

| Posição | Função | Endereço |
|---|---|---|
| 1ª | START | `%IX0.0` |
| 2ª | STOP | `%IX0.1` |
| 3ª | RECONHECER | `%IX0.2` |
| 4ª (luz) | planta ligada | `%QX0.2` |
| 5ª (luz) | batelada completa | `%QX0.3` |

## Roteiro de teste

1. **Conta certo.** START e conte as peças que passam do sensor. Na quinta a luz
   amarela acende e a trava sobe. Se acender na quarta ou na sexta, o problema é
   `>=` vs `>` ou a posição do sensor.
2. **Não conta duas vezes.** Observe uma peça atravessando o feixe devagar. O
   contador tem que subir **uma** unidade, não uma por ciclo.
3. **Represa mesmo.** Com a batelada completa, as peças seguintes encostam na
   trava e ficam lá. A esteira continua andando por baixo — isso é acumulação,
   não defeito.
4. **Reconhecer libera.** Aperte RECONHECER: a luz apaga, a trava desce, a fila
   escoa e a contagem recomeça do zero.
5. **Segurar o reconhecer não trava a contagem.** Segure a botoeira durante uma
   batelada inteira. Ela deve completar normalmente — se ficasse presa em zero,
   o reset estaria por nível em vez de borda.
