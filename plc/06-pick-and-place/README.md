# 06 — Pick-and-place sequencial

O último cenário da biblioteca, e o único que exigiu a Forja aprender uma
capacidade nova. Os cinco anteriores usavam lógica **combinacional**: cada
saída era uma expressão do estado atual. Um ciclo sequencial não cabe nisso.

| Arquivo | O que é |
|---|---|
| `pick-and-place.forja` | esteira, trava, sensor, unidade de 2 eixos, caçamba, HMI |
| `pick-and-place.st` | o programa |

Pré-requisitos: [01](../01-partida-parada-selo/) (selo) e
[02](../02-intertravamento-emergencia/) (intertravamento por realimentação
física — aqui o conceito volta três vezes).

## Por que este exigiu mexer no simulador

A Forja sabia **empurrar** (pistão), **segurar** (trava) e **apagar** (saída).
Não sabia **prender**. Foi preciso ensinar a física a converter uma peça em
corpo conduzido e devolvê-la depois — a decisão está no
[ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md), e a
alternativa de "adaptar com dois pistões em L" foi rejeitada de propósito:
seria peça empurrada em dois tempos, não pega.

## Máquina de estado por passos

O conceito central. A mesma condição física **significa coisas diferentes**
dependendo de onde no ciclo se está:

> `lowered` no passo 1 quer dizer *"chegou na peça, pode pegar"*.
> `lowered` no passo 5 quer dizer *"chegou no destino, pode soltar"*.

Nenhuma expressão combinacional distingue as duas. É preciso guardar **onde
se está**, e é isso que a variável `passo` faz.

```pascal
CASE passo OF
  ST_DESCE:      IF lowered THEN passo := ST_PEGA;      END_IF;
  ST_PEGA:       IF holding THEN passo := ST_SOBE;      END_IF;
  ST_SOBE:       IF raised  THEN passo := ST_AVANCA;    END_IF;
  ...
```

Os passos são **constantes nomeadas**, não `1..8` soltos. Quem lê o código seis
meses depois precisa saber o que o passo 5 significa sem contar nos dedos.

Em CLPs com suporte a SFC (*Sequential Function Chart*, a quinta linguagem da
IEC 61131-3), este mesmo ciclo seria desenhado como um grafo de passos e
transições. O `CASE` em ST é a mesma ideia, e funciona em qualquer CLP.

## Nenhum passo avança por tempo

A regra que vale para o arquivo inteiro. Toda transição espera **confirmação
física**:

| Passo | Espera |
|---|---|
| desce | `lowered` |
| pega | `holding` |
| sobe | `raised` |
| avança | `advanced` |
| desce | `lowered` |
| solta | `NOT holding` |
| sobe | `raised` |
| recua | `retracted` |

Um programa escrito com temporizadores — *"desce, espera 800 ms, assume que
chegou"* — funciona na bancada e quebra no campo. A pressão de ar cai numa
tarde quente, a peça vem mais pesada, o cilindro engripa: o tempo passa e a
máquina segue como se tivesse chegado.

Repare em dois passos especificamente:

- **`ST_PEGA` espera `holding`.** Não basta mandar a garra ligar; é preciso
  confirmar que pegou. Sem isso o ciclo segue com a garra vazia e "deposita"
  nada no destino. É a falha mais comum de pick-and-place mal escrito, e a mais
  difícil de perceber, porque a máquina continua se movendo bonito.
- **`ST_SOLTA` espera `NOT holding`.** Mesma lógica ao contrário: confirmar a
  soltura, não só o comando de soltar.

## Os três intertravamentos

Nenhum é imposto pelo equipamento — ele obedece a comando ruim, como no mundo
real. Impedir é trabalho do programa.

**1. Não avançar com o eixo embaixo.**

```pascal
advance := running AND raised AND (...)
```

Sem o `AND raised`, o carro varre lateralmente na altura da esteira e derruba
a fila inteira. É o intertravamento entre eixos, e é o motivo de existir um
fim de curso de "no alto" em vez de só um de "embaixo".

**2. A garra só segura na janela certa.**

```pascal
grip := running AND ((passo = ST_PEGA) OR (passo = ST_SOBE)
                  OR (passo = ST_AVANCA) OR (passo = ST_DESCE_DEP));
```

Ligar fora dessa janela pega peça em lugar errado. Desligar dentro dela
derruba a peça no meio do caminho.

**3. A esteira só anda com a célula livre.**

```pascal
belt_run := running AND (passo = ST_AGUARDA) AND NOT s_peca;
```

Alimentar durante o ciclo empurra a próxima peça contra a garra em movimento.

## Saídas fora do CASE, de propósito

As saídas são calculadas **depois** da máquina de estado, como função do passo
— e não espalhadas dentro de cada ramo do `CASE`.

Assim existe **um** lugar que responde *"quando esta saída liga?"*, e fica
impossível dois trechos do código disputarem a mesma bobina. Num `CASE` com
atribuições internas, descobrir por que uma saída ligou exige ler os oito
ramos e torcer para não ter esquecido nenhum.

## Parar não aborta o ciclo

```pascal
running := (btn_start OR running) AND NOT btn_stop;
```

O `passo` é preservado ao parar. Religou, continua de onde estava.

Abortar e voltar ao início deixaria uma peça pendurada na garra a meio
caminho, e o ciclo seguinte tentaria pegar outra por cima. Entre "continuar de
onde parou" e "recomeçar do zero", a primeira é quase sempre a correta em
máquina que manipula peça.

## Como rodar

1. Forja: **Abrir…** → `plc/06-pick-and-place/pick-and-place.forja`
2. OpenPLC v4: carregue `pick-and-place.st` (passo a passo em
   [`demo/openplc/README.md`](../../demo/openplc/README.md)).
3. **Rodar** na Forja, Start no OpenPLC, e aperte a botoeira START.

| Posição | Função | Endereço |
|---|---|---|
| 1ª | START | `%IX0.0` |
| 2ª | STOP | `%IX0.1` |
| 3ª (luz) | planta ligada | `%QX0.5` |

## Roteiro de teste

1. **Ciclo contínuo.** Peças chegam, são pegas uma a uma e depositadas na
   caçamba. Verificado sob master emulado: **12 ciclos completos em 75 s**, sem
   travar.
2. **Nunca avança com o eixo embaixo.** Observe a transição do passo 4: o carro
   só começa a andar depois de `raised`. Em nenhum quadro ele se move baixo.
3. **Garra vazia trava o ciclo, e é para travar.** Force `%QX0.4` (grip) para 0
   pela tabela de I/O durante o passo de pega. O ciclo **para** no passo 2
   esperando `holding` — em vez de seguir e depositar nada.
4. **Parar no meio preserva o passo.** STOP com a peça pendurada, depois START.
   O ciclo continua de onde estava, sem soltar a peça no caminho.
5. **A esteira espera.** Durante todo o ciclo de transferência a esteira fica
   parada, mesmo com fila esperando.

O caso 3 é o mais instrutivo: uma máquina que **trava** quando algo não
confirmou é melhor que uma que continua fingindo que deu certo.
