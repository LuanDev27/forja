# 04 — Alarme com rearme e sinaleiro

Detectar uma falha é a parte fácil. O que separa um painel bem feito de um
painel irritante é o que acontece **depois** dela.

| Arquivo | O que é |
|---|---|
| `alarme.forja` | esteira, trava, sensor, 5 botoeiras, 2 sinaleiros |
| `alarme.st` | o programa |

Pré-requisitos: [01](../01-partida-parada-selo/) (selo) e
[03](../03-contagem-batelada/) (temporizador, borda).

## O que a planta faz

Se o sensor fica bloqueado por mais de 3 s com a máquina rodando, é atolamento:
a peça parou de andar. A esteira para, o sinaleiro vermelho **pisca**, e o
alarme fica travado.

A botoeira BLOQUEIO fecha a trava e provoca o atolamento de propósito — é como
exercitar a falha sem quebrar nada.

## As duas dimensões de um alarme

Este é o conceito do cenário, e o que mais gera painel mal feito quando é
ignorado. Um alarme não é um booleano. São **dois**:

```pascal
alarm_active : BOOL;   (* a falha ocorreu e não foi rearmada *)
alarm_acked  : BOOL;   (* alguém apertou reconhecer          *)
```

Quatro combinações, e todas significam coisas diferentes:

| Condição física | Reconhecido | Estado | Sinaleiro |
|---|---|---|---|
| ausente | sim | normal | apagado |
| **presente** | **não** | falha nova | **piscando** |
| presente | sim | falha vista, não resolvida | fixo |
| **ausente** | **não** | **sumiu sem ninguém ver** | **piscando** |

A quarta linha é a que quase todo iniciante perde. A falha apareceu, durou dois
segundos, sumiu — e ninguém estava olhando. Se o alarme se apagasse junto com a
condição, esse evento **desapareceria sem deixar rastro**.

É o defeito intermitente clássico: some sozinho, volta na semana seguinte, e
ninguém consegue provar que aconteceu. Manter o alarme travado até o
reconhecimento é o que transforma isso em informação.

## Reconhecer e rearmar não são a mesma coisa

Duas botoeiras separadas de propósito, porque são dois atos diferentes:

**RECONHECER** (*ack*) — "eu vi". Não conserta, não libera, não religa. Só cala
o pisca. Serve para o operador dizer que tomou ciência, e para o próximo alarme
poder chamar atenção de novo.

**REARMAR** (*reset*) — "pode voltar". Limpa o registro e devolve a máquina à
condição de partir.

```pascal
IF trig_reset.Q AND alarm_acked AND NOT sensor THEN
```

O rearme exige **duas** condições:

- `alarm_acked` — alguém viu antes de mandar continuar.
- `NOT sensor` — a falha física sumiu de verdade.

A segunda é a que protege a máquina. Rearmar com a peça ainda atravessada
apagaria o alarme e mandaria a esteira andar contra o atolamento. **Não se
rearma defeito presente** — e o programa tem que impedir, porque na pressa de
retomar produção alguém vai tentar.

Painéis que juntam as duas coisas num botão só acabam ensinando o operador a
apertar sem olhar. Aí o reconhecimento perde a função.

## O pisca, e por que ele carrega informação

```pascal
lamp_alarm := (alarm_active AND NOT alarm_acked AND blink)
           OR (alarm_active AND alarm_acked);
```

Uma lâmpada, três mensagens:

| | Significa |
|---|---|
| **piscando** | falha nova, ninguém viu |
| **fixo** | falha reconhecida, ainda não resolvida |
| **apagado** | normal |

Um operador do outro lado do galpão distingue os três de relance, sem ler tela
nenhuma. Essa convenção é padrão de chão de fábrica e vale de graça — é uma
linha de código a mais.

### O oscilador

O IEC 61131-3 não tem bloco de pisca padrão. O jeito portátil são dois `TON` se
realimentando:

```pascal
t_off(IN := NOT t_on.Q, PT := T#400ms);
t_on(IN := t_off.Q,     PT := T#400ms);
blink := t_off.Q;
```

Seguindo o ciclo: `t_off` conta enquanto `t_on` não estourou. Quando `t_off`
estoura, `blink` sobe **e** `t_on` começa a contar. Quando `t_on` estoura, ele
derruba a entrada de `t_off`, que zera — e com ela `blink` e a própria entrada
de `t_on`. No ciclo seguinte tudo recomeça.

Resultado: 400 ms aceso, 400 ms apagado.

Vale entender a construção em vez de copiar: a mesma ideia gera qualquer clock,
e num CLP sem biblioteca é o que você tem.

## O `AND running` que evita alarme falso

```pascal
jam_timer(IN := sensor AND running, PT := T#3s);
```

Sem o `AND running`, desligar a máquina com uma peça parada na frente do sensor
dispararia atolamento em 3 s — com a esteira já parada, sem nada de errado.

Alarme que dispara sozinho em situação normal é pior que alarme nenhum: o
operador aprende a ignorar, e ignora também o verdadeiro. Toda condição de
alarme precisa da pergunta *"isso é anormal em que contexto?"*.

## Como rodar

1. Forja: **Abrir…** → `plc/04-alarme-rearme/alarme.forja`
2. OpenPLC v4: carregue `alarme.st` (passo a passo em
   [`demo/openplc/README.md`](../../demo/openplc/README.md)).
3. **Rodar** na Forja, Start no OpenPLC.

| Posição | Função | Endereço |
|---|---|---|
| 1ª | START | `%IX0.0` |
| 2ª | STOP | `%IX0.1` |
| 3ª | RECONHECER | `%IX0.2` |
| 4ª | REARMAR | `%IX0.3` |
| 5ª | BLOQUEIO (segure) | `%IX0.4` |
| 6ª (luz) | rodando | `%QX0.2` |
| 7ª (luz) | alarme | `%QX0.3` |

## Roteiro de teste

1. **Alarme dispara.** START, depois segure BLOQUEIO. A peça encosta na trava
   sobre o sensor; após 3 s a esteira para e a luz vermelha **pisca**.
2. **Reconhecer só cala o pisca.** Aperte RECONHECER: a luz vira **fixa**. A
   máquina continua parada — reconhecer não é rearmar.
3. **Não rearma com defeito presente.** Ainda segurando BLOQUEIO, aperte
   REARMAR. **Nada acontece**: a falha física continua lá.
4. **Sumiu sem ninguém ver.** Reinicie, provoque o alarme e solte o BLOQUEIO
   *sem* reconhecer. A peça escoa, o sensor libera — e a luz **continua
   piscando**. A falha não pode sumir do painel sozinha.
5. **Sequência completa.** Solte o BLOQUEIO, RECONHECER, REARMAR, START. Agora
   sim volta a rodar.
6. **Alarme falso.** Com a máquina parada (STOP) e uma peça sobre o sensor,
   espere 10 s. **Não deve alarmar** — é o `AND running` funcionando.

O caso 4 é o coração deste cenário. Se a luz apagar sozinha ali, o alarme está
apenas espelhando um sensor, e não registrando um evento.
