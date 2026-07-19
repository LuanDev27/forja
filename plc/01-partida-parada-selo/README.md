# 01 — Partida/parada com selo

A primeira lógica que se aprende em comando elétrico, e a base de quase todo
o resto. Uma esteira, duas botoeiras e um sinaleiro.

| Arquivo | O que é |
|---|---|
| `partida-parada.forja` | a planta: esteira, botoeiras START e STOP, sinaleiro |
| `partida-parada.st` | o programa, em Texto Estruturado (IEC 61131-3) |

## O problema

Botoeira é **momentânea**: ela fecha o contato enquanto seu dedo está em cima
e abre quando você solta. Se o programa fosse

```
motor := btn_start;
```

a esteira andaria só enquanto você segurasse o botão. Ninguém opera uma planta
assim. O que se quer é: um toque liga, e continua ligado.

## O selo

```
motor := (btn_start OR motor) AND NOT btn_stop;
```

Leia o parêntese como dois caminhos somados para o mesmo destino:

- `btn_start` — liga **agora**, enquanto o dedo está no botão.
- `motor` — continua ligado porque **já estava** ligado.

O `OR motor` é a saída se realimentando. No painel físico isso não é metáfora:
é um **contato auxiliar do próprio contator**, ligado em paralelo com a
botoeira. Quando o contator fecha, esse contato fecha junto e passa a segurar
a bobina sozinho. Por isso o nome — ele *sela* o circuito depois que a botoeira
volta a abrir.

Em ladder, é o desenho mais reconhecível da automação:

```
    START      STOP       MOTOR
 ----| |----+---|/|--------( )----
            |
    MOTOR   |
 ----| |----+
```

O degrau de baixo é o selo. É a mesma coisa que o `OR motor`.

## Parada dominante

Repare onde o `AND NOT btn_stop` está: **fora** do parêntese.

```
motor := (btn_start OR motor) AND NOT btn_stop;   (* certo   *)
motor :=  btn_start OR (motor AND NOT btn_stop);  (* ERRADO  *)
```

Na versão errada, o START está fora do alcance do STOP. Segurando as duas
botoeiras, a máquina **parte** — porque `btn_start OR (...)` já é verdadeiro
pelo primeiro termo. Na versão certa, o STOP corta os dois caminhos ao mesmo
tempo e sempre vence.

Isso se chama **parada dominante**, e não é preferência: é o comportamento
exigido. Uma máquina que ignora o botão de parada porque alguém está com a mão
no de partida é uma máquina que machuca gente.

## NA e NF — a decisão que é de segurança, não de estilo

Este é o conceito mais importante deste cenário, e o que mais separa quem leu
um tutorial de quem entendeu automação.

No arquivo `.st` a parada está escrita como `AND NOT btn_stop`, porque a Forja
modela `hmi.button` como **NA** (normalmente aberto): o bit vai a 1 *enquanto*
o botão está pressionado. Funciona no simulador. **Num painel de verdade,
a botoeira de parada é NF** (normalmente fechada), e a linha vira:

```
motor := (btn_start OR motor) AND btn_stop;   (* NF: sem o NOT *)
```

Parece só ter sumido um `NOT`. O que mudou de verdade é o que acontece quando
**o fio arrebenta**:

| | Botoeira NF (correto) | Botoeira NA (errado para parada) |
|---|---|---|
| Em repouso | contato fechado, entrada = 1 | contato aberto, entrada = 0 |
| Pressionada | abre, entrada = 0 → para | fecha, entrada = 1 → para |
| **Fio partido** | entrada = 0 → **para sozinha** | entrada = 0 → **nunca mais para** |

Com NF, romper o cabo produz o mesmo efeito de apertar o botão: a máquina para.
A falha se manifesta na hora e para o lado seguro. Com NA, o cabo partido deixa
tudo funcionando normalmente — e o defeito só aparece no dia em que alguém
precisa parar a máquina e o botão não responde. É a pior categoria de falha:
a que fica escondida até o momento exato em que era necessária.

Pela razão espelhada, a botoeira de **partida é NA**: fio partido significa
"não parte", que é a falha segura desse lado.

Esse raciocínio — *escolher a falha que você prefere ter* — é o mesmo do
Artigo VII da Forja, que pausa a simulação quando o CLP some em vez de seguir
com valores velhos.

## Selo ou SET/RESET?

Existe outro jeito de travar uma saída, com bobinas *set* e *reset*:

```
IF btn_start THEN motor := TRUE;  END_IF;
IF btn_stop  THEN motor := FALSE; END_IF;
```

Funciona, e em ladder aparece como `-(S)-` e `-(R)-`. Duas diferenças que
importam:

- **Dominância vira ordem de escrita.** No selo, a dominância está explícita na
  expressão. Aqui, quem manda é quem foi escrito por último — se você inverter
  as duas linhas, a partida passa a dominar, e nada no código avisa.
- **Retenção.** Em muitos CLPs a memória de um *set* é retentiva: sobrevive ao
  desligamento e a máquina volta ligada sozinha na energização. Quase sempre é
  o oposto do que se quer.

Por isso o selo é o padrão em partida de motor, e o SET/RESET fica para estado
que realmente deve persistir — alarme reconhecido, receita selecionada.

## Por que aqui não tem R_TRIG

Se você olhar o [`demo/openplc/separador.st`](../../demo/openplc/separador.st),
o botão de lá usa `R_TRIG` (detector de borda de subida). Aqui não. A diferença
vale entender:

- Lá o botão é **alterna** (liga/desliga no mesmo botão). Sem detectar a borda,
  o programa leria "pressionado" a cada ciclo de 50 ms e a saída piscaria
  dezenas de vezes por segundo enquanto o dedo estivesse no botão.
- Aqui são **dois botões, cada um com um sentido só**. Ler o nível é suficiente:
  START ligado várias vezes seguidas continua ligado, e o selo faz o resto.

A regra prática: borda quando a ação **inverte** o estado; nível quando ela
**determina** o estado.

## Como rodar

1. Abra a Forja, **Abrir…** → `plc/01-partida-parada-selo/partida-parada.forja`.
2. No OpenPLC v4, carregue `partida-parada.st` e configure a Forja como
   dispositivo remoto Modbus TCP — o passo a passo detalhado está em
   [`demo/openplc/README.md`](../../demo/openplc/README.md), trocando só o
   arquivo do programa.
3. Entre em **Rodar** na Forja e dê Start no OpenPLC.
4. Clique na botoeira START (a vermelha à esquerda): a esteira anda e o
   sinaleiro acende. Solte — **continua andando**. Esse é o selo.
5. Clique na STOP: para.

Sem OpenPLC dá para conferir a planta na mão: em **Rodar**, use a tabela de I/O
e force `%QX0.0`. Isso testa a cena, não a lógica — o programa só é exercitado
com CLP de verdade.

## O que dá errado sem isso

Tire o `OR motor` e a esteira vira um botão de campainha: anda só enquanto
alguém segura. Tire o `AND NOT btn_stop` e ela liga e nunca mais para, porque
o selo se sustenta sozinho para sempre — a única saída vira desligar a
alimentação, que é exatamente o que a botoeira de parada existe para evitar.
