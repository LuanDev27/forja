# ADR 0005 — Sinal analógico: palavras no contrato de I/O e onde vive o escalonamento

**Status:** Proposta (a ratificar ao abrir `specs/003-analogico`)
**Data:** 2026-07-22
**Motiva:** Fase 2 do [ROADMAP](../ROADMAP.md) (`specs/003-analogico`, a criar)

> Este ADR é um **rascunho** deixado ao fim da sessão de 22/07 para dar
> arranque à Fase 2. As três decisões abaixo tocam a **camada 1** (o contrato
> com o PLC) e mudá-las depois custaria caro — por isso ficam fixadas antes da
> primeira linha de código. Ratificar ou revisar ao formalizar a spec.

## Contexto

A observação **O3** do [ADR 0001](0001-visao-de-integracao-ecossistema.md) já
registrou que o contrato de I/O é **digital-only**. Confirmado por inspeção:

- `IoSnapshot` (`src/Forja.Anvil/Contracts/IPlcDriver.cs:22`) carrega só
  `ReadOnlyMemory<bool> Bits`. É o único ponto de contato do núcleo com o PLC
  (`Exchange(IoSnapshot)`, Artigo IV.1).
- `IoTable` (`src/Forja.Core/Io/IoTable.cs`) guarda `bool[]` de entradas e
  saídas, força bool, e hasheia só bits.
- `PortDef` (`src/Forja.Anvil/Catalog/DeviceTypeDef.cs:21`) é
  `(PortName, Direction)` — **sem tipo de dado**; toda porta é bool implícito.
- `IoMapValidator` (`:65`) **rejeita** `InputRegister`/`HoldingRegister` com o
  erro `analog-not-supported`.

Mas parte do andaime já existe: o enum `IoArea` já tem as áreas de registrador,
`IoAddress.ToIec()` já formata `%IW`/`%QW`, e o `MirrorDataStore` já tem um
`RegisterSource` "tolerado" (aceita o master sondar, mas a simulação nunca
publica nem lê valores ali). A Fase 2 é **fiar palavras por um cano que hoje só
passa bits**, não construir o cano.

Três decisões precisam ser fixadas antes de começar.

## Decisão

### 1. `IoSnapshot` ganha um canal de palavras paralelo aos bits

```csharp
public readonly record struct IoSnapshot(
    ulong TickNumber,
    ReadOnlyMemory<bool>   Bits,
    ReadOnlyMemory<ushort> Words,
    bool Valid = true);
```

Simétrico: no snapshot de **entrada** (sensor→PLC), `Bits` = discrete inputs e
`Words` = input registers; no de **saída** (PLC→atuador), `Bits` = coils e
`Words` = holding registers. Indexado por offset, como os bits já são. Um único
formato serve às duas direções, e o `Exchange` continua sendo uma troca só.

Rejeitado empacotar por device ou usar um dicionário: quebraria o layout plano
indexado por offset de que o hot-path depende, e o determinismo fica mais difícil
de garantir. Palavra é 16 bits porque Modbus é 16 bits — `float` só existe na
camada de unidade de engenharia (ver decisão 2), nunca no fio.

### 2. O escalonamento é **dado da cena, por ponto analógico** — não do device, não do driver

O modelo espelha um cartão de entrada analógica real:

- O **device** produz uma grandeza física em **unidade de engenharia** (um sensor
  de nível dá 0–100 cm), declarada como parâmetro (`euMin`/`euMax`).
- O **ponto analógico na cena** carrega a faixa **bruta** do "cartão"
  (`rawMin`/`rawMax`, padrão `0..65535`).
- A **fronteira de I/O** (`IoTable`) converte EU→bruto nas entradas e bruto→EU
  nas saídas. O registrador carrega **bruto**; o programa de CLP escala de volta
  em ST (`%IW → EU`), exatamente como no chão de fábrica.

Fica explícito, versionado, inspecionável na Tabela de I/O, e configurável por
instância (dois sensores iguais podem ter cartões diferentes) — mantém
**cena é dado** (Artigo III). Ensina a coisa toda: o escalonamento do lado da
Forja (o cartão) **e** o do lado do CLP (o programa).

### 3. `PortDef` ganha tipo de dado; `schemaVersion` vai a 2 de forma **aditiva**

`PortDef` ganha `PortType { Bool, Word }`, padrão `Bool`. Os 18 dispositivos
atuais seguem `Bool` sem tocar em nada; os analógicos novos declaram `Word`.

`CurrentSchemaVersion` vai de 1 para 2. Como o analógico é **puramente aditivo**
(cenas v1 não têm ponto analógico nem faixa de escala), uma cena v1 **carrega**
numa Forja v2 com os campos novos preenchidos por padrão — sem migração
destrutiva. A falha explícita do Artigo III fica reservada a um campo v2
malformado, não à ausência dele.

### 4. Valores analógicos entram no hash (determinismo)

`IoTable.WriteState` passa a hashear as palavras em ordem de endereço, junto dos
bits (Artigo I.3). Como o fio é `ushort` bruto, o hash é determinístico; o `float`
de EU nunca entra no estado. Sem isso, duas execuções idênticas com um sensor
analógico divergiriam sem o hash acusar — o modo de falha que o Artigo V existe
para pegar.

## Consequências

**Assumidas:**

- Toque em camada 1: `IoSnapshot`, `IoTable`, `MirrorDataStore` e o modo cliente
  do driver mudam juntos. É o maior exercício de arquitetura do projeto, e o
  custo é consciente.
- Mais superfície: tipo de porta, faixa de escala, força numérica na UI, e o
  caminho de palavras no double-buffer do `MirrorDataStore`.
- `float` (EU) e `ushort` (bruto) coexistem — a conversão vive num só lugar
  (a fronteira `IoTable`) para não vazar.

**Ganhas:**

- Sensores e atuadores que só fazem sentido em analógico passam a existir
  (nível, peso, velocidade variável), e com eles a lógica de comparação/setpoint
  — vocabulário direto de vaga ("sinal analógico", "escalonamento", "instrumentação").
- Destrava tudo adiante: sem analógico, qualquer SCADA ou dashboard das próximas
  fases mostraria só lâmpada acesa/apagada (O3 do ADR 0001).
- A migração de schema vira exercício real e sem trauma (aditiva), servindo de
  molde para as próximas.

## Alternativas rejeitadas

**Escala só no CLP (a Forja manda bruto sobre faixa fixa do device).** Mais
simples e realista num ponto (cartão analógico só dá contagem crua). Rejeitada
porque esconde o conceito de escalonamento que a fase existe para ensinar, e
tira a configuração de cartão da cena — que é justamente o que um técnico
configura no campo.

**Escala no device.** Ataria a faixa bruta ao tipo, não à instância — dois
sensores iguais não poderiam ter cartões diferentes. A faixa bruta é
propriedade do canal (cena), não do sensor.

**Canal de palavras por dispositivo (dicionário device→registers).** Mais
"orientado a objeto", mas quebra o layout plano por offset de que o hot-path e o
hash dependem. Rejeitado por custo de determinismo.

**Migração destrutiva v1→v2 (reescrever cenas antigas).** Desnecessária: como o
analógico é aditivo, cena v1 carrega direto. Migração destrutiva só se
justificaria se um campo v1 mudasse de significado, o que não ocorre aqui.
