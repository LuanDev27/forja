# 07 — Controle de nível

O primeiro cenário da biblioteca em que o CLP **age sobre um número**. Os seis
anteriores liam bits e escreviam bits — ligado/desligado, presente/ausente. Aqui
entra a outra metade da automação: a medida contínua.

| Arquivo | O que é |
|---|---|
| `controle-nivel.forja` | silo com alimentador de correia, sensor de nível, emissor e caçamba |
| `controle-nivel.st` | o programa |

Pré-requisito: nenhum dos anteriores. Este cenário é deliberadamente a lógica
mais simples possível — a novidade toda está no **tipo de sinal**, não na lógica.

## A planta: alimentador de correia sob silo

O vaso **não tem fundo próprio** — o fundo dele é a própria esteira. É o
arranjo clássico de *belt feeder*: quatro paredes sobre a correia, com a parede
da frente (a **comporta**) suspensa a 50 cm acima da correia. O material se
acumula dentro; a correia arrasta a camada de baixo por debaixo da comporta.

```
        emissor
           |
           v          sensor de nível (%IW0)
    ┌──────┬───────┐      |
    │      │       │      v            comporta suspensa
    │  material acumulado │           ┌──────
    │                     │           │
    ════════════════════════════════════════════>  esteira %QW0
```

Consequência direta: **a velocidade da esteira é a vazão de saída**. Rápido
esvazia, devagar enche. É o que fecha a malha — sem o vaso, mudar a velocidade
não mudaria nível nenhum.

Geometria (em `controle-nivel.forja`): correia de 4 m com topo em Y=0,25;
paredes de 0,8 m formando um vaso de 1,0 × 0,55 m; comporta com o bordo
inferior em Y=0,75. O sensor fica em Y=1,25 com alcance 1,0 m, então mede a
coluna de material de 0 a 1 m acima da correia — 100 % é material até a altura
do sensor, e o vaso cheio até a borda dá ~80 %.

## O mapa de endereços

| Forja | IEC | O quê |
|---|---|---|
| input register 0 | `%IW0` | nível do pulmão, bruto 0..65535 |
| holding register 0 | `%QW0` | velocidade da esteira, bruto 0..65535 |

A cena declara o que esses brutos significam: o sensor cobre 0–100 % e a esteira
0–2 m/s. O CLP **não recebe** essa informação — ele recebe 16 bits e mais nada.

## Bit e palavra são canais separados

`%IX0.0` e `%IW0` não disputam espaço: discrete inputs e input registers são
áreas Modbus diferentes. Um cenário pode ter os dois ao mesmo tempo sem que um
endereço atropele o outro. É por isso que a Fase 2 pôde ser aditiva — nenhum dos
cenários 01–06 precisou mudar de endereço.

## O que muda quando o sinal é uma palavra

### 1. O tipo importa, e erra feio

`%IW` é uma palavra **sem sinal**: 0..65535. Declarada como `INT` (com sinal,
−32768..32767), toda leitura acima da metade da escala vira número negativo — o
tanque passa de 50 % e o programa acha que ele está vazio. Por isso o programa
usa `UINT`.

### 2. Reescalar é trabalho do programa

```pascal
nivel_pct := DINT_TO_UINT(UINT_TO_DINT(nivel_raw) * 100 / 65535);
```

Duas armadilhas na mesma linha:

- **multiplicar antes de dividir**, ou a divisão inteira joga a precisão fora;
- **alargar para `DINT` antes de multiplicar**, ou `65535 * 100` estoura os
  16 bits.

Uma das duas correções sozinha estraga a outra.

### 3. Banda morta — o conceito central

A versão ingênua funciona por alguns segundos:

```pascal
IF nivel_pct >= SP_NIVEL THEN vel := RAPIDA; ELSE vel := LENTA; END_IF;
```

Depois a esteira começa a chacoalhar entre 0,5 e 1,0 m/s várias vezes por
segundo. Um sinal analógico real **nunca fica parado**: ruído elétrico, ondulação
da superfície do material e a própria quantização do cartão fazem a leitura
tremer em torno do setpoint. Comparação sem banda morta transforma esse tremor em
chaveamento, e chaveamento a 20 Hz destrói contator, inversor e paciência.

A banda morta separa o ponto de ligar do ponto de desligar:

```
   liga a drenagem rápida em 65 %   (SP + BANDA)
   volta para devagar     em 55 %   (SP - BANDA)
```

Entre 55 e 65 **nada muda**: a saída fica onde estava. Isso obriga a saída a ter
memória — `drenando` é uma variável, não uma expressão, porque a resposta depende
de onde o processo *veio*, não só de onde ele está.

O teste headless prova exatamente isso: alimentado com ruído de **uma contagem**
em cima da fronteira do setpoint, o controlador ingênuo chaveia ~39 vezes em 40
ticks; o com banda morta não chaveia nenhuma.

### 4. Quantização não é detalhe

O setpoint de 60 % cai em 39321 contagens (`65535 × 0,6`). Uma contagem para
baixo — 39320 — já reescala para 59 %. Ou seja: a fronteira entre "abaixo" e
"acima" do setpoint é uma linha de **uma contagem** de largura, e o sinal cruza
essa linha o tempo todo. É o mesmo problema que o filtro de repique resolve no
[cenário 03](../03-contagem-batelada/), na versão analógica.

## Por que duas velocidades e não um PID

O objetivo aqui é provar o **ciclo completo** — ler número, decidir sobre número,
escrever número. Um PID acrescentaria sintonia, anti-windup e tempo de
amostragem a um cenário que ainda está apresentando o tipo de dado. O caminho
para ele fica aberto: o `%QW0` já aceita qualquer valor de 0 a 65535.

## Duas coisas que a planta ensinou (e não estavam no plano)

**1. Silo arqueia.** Na primeira montagem a comporta deixava 30 cm de vão —
uma camada e meia de peça. O material formou **arco** sobre a abertura e parou
de escoar: a correia corria vazia por baixo enquanto o nível ficava travado em
84 %, com o programa comandando drenagem rápida e nada acontecendo. É
exatamente a patologia de silo real, e ela apareceu sozinha na física. Abrir a
comporta para 50 cm resolveu.

**2. Sensor pontual sobre material granular é ruidoso — e isso derrubou a
malha.** O `sensor.level` lança **um raio**. Quando esse raio cai numa fresta
entre duas peças, ele atravessa até a correia e o nível lê **0** mesmo com o
vaso meio cheio.

Medido na bancada, o estrago foi este: **~30 trocas de velocidade em 60 s**,
cada estado durando 0,1 a 0,5 s. A esteira chacoalhando, não controlando.

```
17,0s  nivel=95% -> RAPIDA
17,2s  nivel=48% -> lenta      <- 0,2 s depois
17,5s  nivel=92% -> RAPIDA
17,7s  nivel=40% -> lenta
```

A banda morta **não** conserta isso. Ela filtra tremor de *quantização* em
torno do setpoint; aqui o problema é *impulso* — a leitura despenca a zero e
volta. São dois ruídos diferentes, e confundir os dois leva a aumentar a banda
morta até o controle ficar surdo.

O conserto certo mora no **instrumento**, não no programa: o parâmetro
`damping` do sensor, que publica a **mediana** das últimas N leituras. Mediana
e não média, de propósito — média espalharia o zero por toda a janela, mediana
simplesmente o descarta enquanto ele for minoria. É o mesmo motivo pelo qual
transmissor de nível de verdade tem amortecimento na folha de dados.

Com `damping: 45` (0,75 s a 60 Hz) e a marcha rápida recalibrada para 1,0 m/s,
a malha passou a fazer ciclos de **10 a 17 segundos**:

```
 7,4s  nivel=77% -> RAPIDA 1,0 m/s
21,8s  nivel=41% -> lenta 0,5 m/s     <- 14,4 s de drenagem
```

## Validar

O cenário roda headless em `tests/Forja.Core.Tests/LevelControlLoopTests.cs`,
contra um CLP de mentira que espelha este `.st` linha por linha — inclusive o
teste de determinismo (mesmo percurso de nível, mesmo hash).

Para a validação no OpenPLC Editor, a tabela de variáveis está em
[`../openplc-editor-tabelas.md`](../openplc-editor-tabelas.md). Atenção: o board
**OpenPLC Simulator** expõe só `%IX0.0–0.7` e `%QX0.0–0.7`; para localizar
`%IW0`/`%QW0` é preciso um board que declare áreas de registrador.
