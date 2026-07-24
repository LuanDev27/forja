# Bancada: exercitar a Forja por Modbus, sem CLP

```powershell
.\tools\bancada\subir-forja.ps1        # sobe a Forja headless, cena 07, em Run
.\tools\bancada\master-nivel.ps1       # master Modbus rodando a lei do .st
```

O `master-nivel.ps1` é um **master Modbus TCP descartável**: conecta na 5020, lê
o `%IW0` (FC04), roda a mesma lógica do
[`plc/07-controle-de-nivel/controle-nivel.st`](../../plc/07-controle-de-nivel/)
— reescala, banda morta, duas velocidades — escreve o `%QW0` (FC06) e lê de
volta (FC03) para conferir.

Frames Modbus crus, montados na mão, sem biblioteca: o objetivo é justamente
não deixar uma abstração mascarar o que trafega no fio.

## Para que serve

O teste headless (`tests/Forja.Core.Tests/LevelControlLoopTests.cs`) prova a
malha contra um CLP de mentira **dentro do processo**. Este script prova a
mesma malha **atravessando TCP**: serialização das palavras, FC04/FC06/FC03,
as duas direções do double-buffer, e o fail-safe de timeout.

É o degrau entre o teste headless e o CLP de verdade — e não depende de ter o
OpenPLC configurado.

## Resultado de referência (23/07/2026)

```
ciclo | %IW0 (nivel) | pct | drenando | %QW0 escrito | %QW0 lido
   30 |        55241 |  84 |     True |        49151 | 49151
   35 |        28464 |  43 |    False |        16384 | 16384
```

A malha atravessou a banda morta **nos dois sentidos** sobre o fio: subiu de 43%
para 84% (passou de `SP+BANDA` = 65) e comandou a drenagem rápida; caiu para 43%
(abaixo de `SP-BANDA` = 55) e voltou para a lenta. O `%QW0` lido de volta bate
com o escrito nos dois casos.

## Duas armadilhas

**1. O timeout do master pausa a simulação — e ela NÃO volta sozinha.**
A cena declara `timeoutMs: 1000`. Se o master ficar mais que isso sem falar, a
Forja entra em falha e **pausa** (Artigo VII.1 — não seguir com setpoint velho).
Quando o master volta, o *driver* se recupera (`master reconectado`), mas o modo
continua em **Pause**: os sensores congelam no último valor e o `%IW0` para de
variar. Sintoma típico: "o nível travou em 0". Solução: reiniciar a cena, ou
mandar Run pela UI. Ver o `.err` do log — a falha aparece lá.

**2. `-shl` sobre `[byte]` no PowerShell 5.1 mascara em 8 bits.**
`[byte]0xBF -shl 8` dá `0`, não `0xBF00`. Sem um `[int]` na frente, todo
registrador lido volta só com o byte baixo — 49151 vira 255, 16384 vira 0. Isso
custou meia hora fingindo ser um defeito da Forja. O `master-nivel.ps1` confere
o *transaction id* de cada resposta justamente para que dessincronização de
frame apareça como erro em vez de valor errado.

## Limite conhecido da cena 07

O nível só sobe quando peças passam sob o sensor — a cena **não tem um vaso**
onde o material se acumule. Os picos de 84% são peças cruzando o feixe, não uma
coluna de material. Para uma demonstração honesta de controle de nível falta
montar o pulmão com corpos estáticos; enquanto isso, a cena serve para provar o
canal analógico, não a dinâmica do processo.

## Parâmetros

| | |
|---|---|
| `-Ciclos` | quantas iterações (padrão 40) |
| `-IntervaloMs` | período do master (padrão 200; use ≤ 500 para não estourar o timeout) |
| `-ForcarVel <bruto>` | ignora a lei de controle e escreve esse valor no `%QW0` |
| `-Hex` | loga cada frame enviado e recebido |
