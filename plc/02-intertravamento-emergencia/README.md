# 02 — Intertravamento e emergência

O cenário 01 ensinou a **ligar** uma máquina. Este ensina a **impedir** que ela
faça besteira — que é a maior parte do trabalho real de quem programa CLP.

| Arquivo | O que é |
|---|---|
| `intertravamento.forja` | esteira, pistão desviador, sensor, 4 botoeiras, 2 sinaleiros |
| `intertravamento.st` | o programa |

Pré-requisito: o [cenário 01](../01-partida-parada-selo/), porque o selo de
partida aparece aqui de novo, agora subordinado à emergência.

## Antes de tudo: emergência não se faz em software

Este é o ponto profissional mais importante do cenário, e é onde muito material
de estudo mente por omissão.

Uma parada de emergência de verdade é **circuito cabeado**: a botoeira NF, um
relé de segurança e o corte da alimentação dos atuadores. O CLP **não está no
caminho** dessa proteção. Se estivesse, a segurança dependeria do programa não
travar, do scan não atrasar, da memória não corromper — e é justamente isso que
uma emergência precisa sobreviver.

O que o CLP faz, e é o que está neste arquivo:

- **Sabe** que a emergência ocorreu (lê o contato).
- **Não religa sozinho** quando ela é liberada.
- **Sinaliza** o estado para o operador.

Se numa entrevista te perguntarem "como você faria uma emergência?", a resposta
que impressiona não é o código — é *"a emergência é cabeada, com relé de
segurança; no CLP eu trato o intertravamento e o rearme"*.

## O intertravamento

Duas coisas nesta planta não podem acontecer juntas: a esteira andando e o
pistão avançado. Se andarem juntas, a peça trava contra a haste.

```pascal
belt_run      := running AND NOT diverting AND NOT piston_extended;
piston_extend := diverting AND NOT belt_run AND NOT emergency;
```

Cada saída carrega a negação da outra. Isso é o intertravamento: não é uma
sequência, é uma **proibição mútua** que vale a cada ciclo, independente do
estado em que a máquina esteja.

### Intertrave contra o sensor, não contra o comando

A linha mais importante do arquivo é o `NOT piston_extended`, e o motivo está em
**qual informação ela consulta**.

| Fonte | O que ela diz | Serve para intertravar? |
|---|---|---|
| `piston_extend` (a saída) | "eu **mandei** recolher" | **não** |
| `piston_extended` (o sensor) | "ele **está** recolhido" | sim |

A diferença entre as duas frases é o mundo físico. A mangueira pode ter furado,
o ar da rede pode ter caído, a haste pode estar emperrada numa peça atravessada.
Nesses casos o comando diz que está tudo bem e o sensor diz a verdade.

Intertravar contra a própria saída é escrever um teste que só verifica a própria
intenção — sempre passa, e não protege de nada.

### Ordem das linhas importa

`piston_extend` usa `NOT belt_run`, e `belt_run` é calculado **na linha de cima**.
Em ST as atribuições rodam de cima para baixo dentro do ciclo, então o pistão
enxerga o valor da esteira **já atualizado neste ciclo**, não o do ciclo
anterior. Em ladder é idêntico: vale a ordem dos degraus.

Trocar as duas linhas de lugar muda o comportamento e introduz um atraso de um
ciclo. Com 50 ms de scan, isso é a diferença entre intertravar e *quase*
intertravar.

## A emergência travada

```pascal
IF btn_emerg THEN
  emergency := TRUE;
END_IF;

trig_reset(CLK := btn_reset);
IF trig_reset.Q AND NOT btn_emerg THEN
  emergency := FALSE;
END_IF;
```

Três decisões aqui, e nenhuma é arbitrária:

**A trava é por nível.** Enquanto a botoeira estiver apertada, `emergency` é
reafirmado a cada ciclo. Não adianta apertar outro botão junto para escapar.

**O rearme é por borda** (`R_TRIG`). Se fosse por nível, daria para segurar o
rearme com um dedo, soltar a emergência com o outro, e a máquina se liberaria no
mesmo instante — burlando toda a proteção com uma mão. Com borda, o rearme só
conta no momento em que é pressionado, e nesse momento a emergência precisa já
estar liberada.

**Liberar não é ligar.** Rearmar só apaga `emergency`. A máquina fica parada
esperando o START, porque `running` foi derrubado e o selo precisa ser refeito.
Isso é o **rearme obrigatório**: nada volta a se mexer sem uma ação deliberada
de alguém que olhou para a máquina.

O acidente que essa regra evita tem nome — *restart surprise*. Alguém aperta a
emergência para desatolar uma peça, resolve, solta a botoeira, e a máquina volta
a andar com a mão dele lá dentro.

## Partida a frio

```pascal
emergency : BOOL := TRUE;
```

A variável **nasce travada**. Ao energizar o CLP, a máquina está em emergência e
exige rearme antes de qualquer coisa.

É deliberado: a energização é exatamente o momento em que ninguém sabe o que
aconteceu antes — pode ter faltado luz no meio de um ciclo, com peça atravessada
e alguém com a mão na máquina. Exigir um rearme consciente força uma pessoa a
olhar para a planta antes de liberar.

Inicializar como `FALSE` faria a máquina energizar pronta para partir. Parece
mais cômodo. É a mesma classe de erro do fio partido no cenário 01: troca
segurança por conveniência num lugar onde não se troca.

## NA e NF, de novo

Como no cenário 01, a botoeira de emergência está lida como **NA**
(`IF btn_emerg THEN`) porque é assim que a Forja modela `hmi.button`. Num painel
real, botoeira de emergência é **NF, sempre, sem exceção** — inclusive por norma
(IEC 60947-5-5, contato de abertura positiva). A linha viraria:

```pascal
IF NOT btn_emerg_nf THEN   (* entrada 0 = apertada OU fio partido *)
  emergency := TRUE;
END_IF;
```

Cabo partido, borne solto ou botoeira arrancada produzem entrada 0, que é
tratado como emergência apertada. A falha do sensor de segurança **provoca** a
segurança em vez de anulá-la.

## Como rodar

1. Forja: **Abrir…** → `plc/02-intertravamento-emergencia/intertravamento.forja`
2. OpenPLC v4: carregue `intertravamento.st` e aponte para a Forja como
   dispositivo remoto Modbus TCP (passo a passo em
   [`demo/openplc/README.md`](../../demo/openplc/README.md)).
3. **Rodar** na Forja, Start no OpenPLC.

As botoeiras estão na ordem, da esquerda para a direita:

| Posição | Função | Endereço |
|---|---|---|
| 1ª | START | `%IX0.0` |
| 2ª | STOP | `%IX0.1` |
| 3ª | EMERGÊNCIA | `%IX0.2` |
| 4ª | REARME | `%IX0.3` |
| 5ª (luz) | planta ligada | `%QX0.2` |
| 6ª (luz) | emergência ativa | `%QX0.3` |

## O roteiro de teste

Não basta ver a esteira andar. Estes são os casos que provam a lógica:

1. **Partida a frio.** Energize e aperte START direto. **Nada deve acontecer** —
   a máquina nasce travada. Aperte REARME, depois START: agora anda.
2. **Intertravamento.** Deixe rodando e espere uma peça passar pelo sensor. A
   esteira **para** antes de o pistão avançar, e só volta a andar depois que o
   fim de curso indica haste recolhida. Em nenhum quadro os dois estão ativos.
3. **Emergência durante o ciclo.** Aperte EMERGÊNCIA com o pistão avançado.
   Tudo desliga e a luz vermelha acende.
4. **Emergência não religa.** Solte a botoeira. **Nada volta a andar.** Essa é a
   diferença entre parada e emergência.
5. **Rearme não liga.** Aperte REARME. A luz vermelha apaga, a máquina continua
   parada. Só o START liga.
6. **Tentativa de burla.** Segure o REARME, e só então solte a EMERGÊNCIA. A
   máquina deve continuar travada, porque o rearme é por borda e a borda já
   passou. Solte e aperte o rearme de novo para liberar.

O caso 6 é o que separa um intertravamento escrito com cuidado de um escrito
às pressas — e é o tipo de teste que ninguém faz até alguém se machucar.
