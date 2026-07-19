# 05 — Pulmão com liberação por espaço

Duas máquinas nunca trabalham exatamente no mesmo ritmo. O pulmão é o que
absorve essa diferença — e controlá-lo é decidir **quando** deixar a próxima
peça seguir.

| Arquivo | O que é |
|---|---|
| `pulmao.forja` | esteira, trava, três sensores, 2 botoeiras, 2 sinaleiros |
| `pulmao.st` | o programa |

Pré-requisitos: [01](../01-partida-parada-selo/) (selo) e
[03](../03-contagem-batelada/) (contador, borda).

## A planta

Uma esteira com uma trava no meio. Antes dela as peças se acumulam — esse é o
pulmão. Depois dela existe uma **zona** que comporta uma peça por vez. A trava
só libera quando a zona está livre, e libera **uma** peça.

```
  emissor   S_entrada       S_espera │trava│      S_zona           calha
     ↓          ↓        ▓▓▓▓▓▓▓ ↓   │     │        ↓                ↓
  ══════════════════════════════════════════════════════════════════════
   -2,00      -1,00      acumulação  0,20          1,20            3,16
```

A distância entre o emissor e o sensor de entrada não é decorativa: com ele a
50 cm da queda, a peça ainda estava quicando ao cruzar o feixe e uma única peça
gerava duas bordas. A 1 m ela já assentou. Sensor de contagem quer peça
estabilizada — e essa é uma decisão de **layout**, não de programa.

## Soltar UMA peça é um problema de verdade

Este é o conceito que dá nome ao cenário, e o que quase todo mundo subestima.

A condição de liberação é óbvia: tem peça esperando **e** a zona está livre
**e** a máquina está rodando. O que não é óbvio é que **abrir a trava enquanto
essa condição for verdadeira solta a fila inteira**. A primeira peça sai, a
segunda encosta e passa junto, porque a trava ainda está aberta.

A solução é o `TP` — temporizador de **pulso**:

```pascal
release(IN := release_req, PT := T#700ms);
gate_close := NOT release.Q;
```

`TP` liga a saída na **borda de subida** da entrada e a mantém ligada por um
tempo fixo, **ignorando o que acontece com a entrada durante esse tempo**. É a
diferença crucial em relação ao `TON`, que segue a entrada.

Os 700 ms saem de conta, não de tentativa: a 0,5 m/s são 35 cm de deslocamento
— o suficiente para uma peça de 20 cm atravessar e a seguinte ainda não
alcançar a trava.

Esse número **depende da mecânica**. Esteira mais rápida ou peça maior exige
recalcular. É a segunda vez nesta biblioteca que um tempo do programa amarra
numa dimensão física (a primeira foi o filtro de repique do
[cenário 03](../03-contagem-batelada/)) — e essa dependência não aparece em
lugar nenhum do código. Vale comentar, como está comentado aqui.

## Os três temporizadores da IEC, e quando usar cada um

Chegamos ao terceiro. Vale a comparação:

| Bloco | Comportamento | Usado em |
|---|---|---|
| `TON` | liga a saída **depois** de a entrada ficar N estável | detectar atolamento (04), filtrar repique (03) |
| `TP` | pulso de duração fixa na borda, **ignora** a entrada durante | liberar uma peça (aqui) |
| `TOF` | mantém a saída **por** N depois de a entrada cair | prolongar sinal curto |

A pergunta que escolhe: *quero confirmar que algo durou* (`TON`), *quero uma
ação de duração fixa* (`TP`), ou *quero esticar um sinal que acabou* (`TOF`)?

## Contador bidirecional

```pascal
occupancy(CU := s_entry AND running,
          CD := s_zone  AND running,
          R  := NOT running,
          PV := BUFFER_MAX);
```

`CTUD` conta nos dois sentidos: `CU` soma na borda (peça entrou), `CD` subtrai
(peça saiu). `CV` é a ocupação atual, `QU` avisa cheio, `QD` avisa vazio.

**A ocupação é deduzida, não medida.** Não existe sensor de "quantas peças tem
na fila" — o que existe são duas contagens e uma subtração.

Duas consequências, e a primeira quase sempre é confundida com defeito.

### O que o contador realmente conta

`CV` é o número de peças **entre os dois sensores** — o que inclui as que estão
apenas passando. Isso não é erro: é a definição.

Medindo nesta cena, com a trava aberta e o fluxo livre por 60 s:

```
sensor ENTRADA (x=-1,00): 28 bordas
sensor ESPERA  (x= 0,05): 27 bordas
sensor ZONA    (x= 1,20): 26 bordas
```

As contagens não batem, e **está certo**. Entre a entrada e a zona há 2,2 m; a
0,5 m/s são 4,4 s de percurso, e com peça a cada 2 s isso dá duas peças em
trânsito a qualquer instante. A diferença de 28 para 26 é exatamente essa.

Quem vê `CV = 2` com a fila visivelmente vazia costuma concluir que o contador
está quebrado. Não está — ele está contando o transporte. Se você quer contar
só a fila parada, o sensor de saída precisa ficar logo depois da trava, e não
lá adiante.

### O erro que se acumula de verdade

Além do trânsito, existe deriva real: peça retirada na mão, sensor que perdeu
uma leitura, duas peças coladas que geraram uma borda só, energização no meio
da produção. Esse erro **não se corrige sozinho**.

Por isso todo pulmão real precisa **ressincronizar**. Aqui é o
`R := NOT running` — parar a máquina zera a conta, assumindo que quem parou vai
esvaziar antes de religar. É uma suposição, e está escrita para poder ser
questionada.

E é por isso que a **liberação não depende do contador**: ela usa `s_waiting` e
`zone_free` direto. O contador só acende a luz de "cheio". Deixar o controle
dependendo de um número que deriva seria construir sobre areia.

## Zona livre precisa ser estável

```pascal
zone_free_tmr(IN := NOT s_zone, PT := T#500ms);
```

Não basta o sensor estar apagado neste ciclo. A peça que acabou de sair pode
estar com a traseira ainda no limite do feixe, ou o feixe pode piscar entre
duas peças coladas. Liberar nesse instante coloca a peça nova em cima da
anterior.

Exigir feixe livre **e estável** por 500 ms é barato e resolve. É o mesmo
raciocínio do filtro de repique, aplicado a uma decisão em vez de a uma
contagem.

## A trava fecha por padrão

```pascal
gate_close := NOT release.Q;
```

O estado de repouso é **fechado**. A trava só abre durante o pulso.

Se o programa parar, se o CLP cair, se o pulso nunca vier — a fila fica
represada. O contrário (aberta em repouso) faria a linha inteira escorrer para
a zona de jusante no primeiro problema. É a mesma escolha de falha segura que
aparece na botoeira NF do [cenário 01](../01-partida-parada-selo/) e na
emergência do [02](../02-intertravamento-emergencia/): decidir **para que lado
a coisa falha** antes de decidir como ela funciona.

## Como rodar

1. Forja: **Abrir…** → `plc/05-pulmao-semaforo/pulmao.forja`
2. OpenPLC v4: carregue `pulmao.st` (passo a passo em
   [`demo/openplc/README.md`](../../demo/openplc/README.md)).
3. **Rodar** na Forja, Start no OpenPLC.

| Posição | Função | Endereço |
|---|---|---|
| 1ª | START | `%IX0.0` |
| 2ª | STOP | `%IX0.1` |
| 3ª (luz) | rodando | `%QX0.2` |
| 4ª (luz) | pulmão cheio | `%QX0.3` |

## Roteiro de teste

1. **Uma peça por vez.** START e observe a trava. Cada abertura deve deixar
   passar **exatamente uma** peça. Se passarem duas coladas, o pulso de 700 ms
   está longo demais para a velocidade da esteira.
2. **O pulmão acumula.** Como o emissor solta peça a cada 1,2 s e a liberação
   depende da zona, a fila cresce atrás da trava. É para ser assim — o pulmão
   está fazendo o trabalho dele.
3. **Cheio acende.** Com cinco peças represadas, a luz amarela acende.
4. **Espera de verdade.** Enquanto houver peça sobre o sensor da zona, a trava
   **não** abre, mesmo com fila esperando.
5. **Parar zera.** STOP e START de novo: a contagem de ocupação reinicia.
