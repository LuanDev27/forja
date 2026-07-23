# Feature Specification: Sinais analógicos

**Feature Branch**: `003-analogico`

**Created**: 2026-07-23

**Status**: Draft

**Input**: User description: "Fase 2 — sinais analógicos. Levar a Forja de digital-only para digital + analógico: palavras de 16 bits no contrato de I/O paralelas aos bits, escalonamento unidade de engenharia (EU) ↔ bruto por ponto analógico da cena (equivalente a um cartão 4-20 mA), e a primeira lógica de CLP que compara/age sobre um número. Fatia demonstrável: cena de controle de nível ponta a ponta — sensor de nível analógico (%IW) → programa ST com setpoint/comparação → atuador de velocidade variável (%QW) — validada contra o OpenPLC v4 real."

## Contexto

A Forja sabe ligar e desligar. Todo sensor devolve um bit, todo atuador consome
um bit, e o contrato de I/O — o único ponto de contato do núcleo com o CLP —
carrega apenas booleanos. Foi assim de propósito nas Fases 0 e 1: bit é o
denominador comum e provou o cano ponta a ponta contra o OpenPLC real.

Mas a maior parte do vocabulário de uma vaga de automação vive em cima de um
**número**, não de um bit: "sinal analógico", "4-20 mA", "escalonamento",
"setpoint", "instrumentação". Enquanto a bancada só fala bit, nenhuma dessas
palavras é demonstrável — e qualquer painel ou SCADA das fases seguintes
mostraria só lâmpada acesa/apagada.

Esta fase leva a Forja de **digital-only** para **digital + analógico**: uma
palavra de 16 bits trafega pelo mesmo cano por onde o bit já trafega, um ponto
analógico da cena descreve o "cartão" que converte a grandeza física em contagem
bruta (e de volta), e o programa de CLP passa a **comparar e agir sobre um
valor**, não sobre um estado.

As decisões de arquitetura — onde a palavra entra no contrato, onde vive o
escalonamento, como o schema evolui sem quebrar cenas antigas, e como o
determinismo é preservado — estão fixadas no
[ADR 0005](../../adr/0005-sinal-analogico-e-o-contrato-de-io.md). Esta spec
define **o que** precisa existir e **como se prova** que existe; o ADR responde
**por que assim**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ler um valor analógico num CLP (Priority: P1)

Quem monta a planta coloca um sensor analógico (por exemplo, um sensor de nível)
numa cena, mapeia a porta dele para um endereço de registrador de entrada
(`%IW`), entra em modo Rodar e vê o valor bruto aparecer na tabela de I/O.
Quem programa CLP aponta o OpenPLC para a cena, lê esse `%IW` no programa e
escala a contagem bruta de volta para a unidade de engenharia (0–100 cm) em ST.

**Why this priority**: é a metade de entrada do analógico inteiro — sem ler um
número, nada mais existe. É demonstrável sozinha: um sensor que publica um valor
que o CLP lê e escala já ensina o conceito central da fase (bruto ↔ EU), sem
precisar de nenhum atuador analógico.

**Independent Test**: montar uma cena mínima com um sensor de nível cujo valor
físico é conhecido, rodar N ticks headless, e assertar que o registrador de
entrada carrega a contagem bruta esperada para aquela grandeza segundo a faixa
do cartão. Ponta a ponta: apontar o OpenPLC v4, ler o `%IW` e conferir que o
programa reconstrói a EU.

**Acceptance Scenarios**:

1. **Given** um sensor de nível com faixa de engenharia 0–100 cm mapeado num
   `%IW` com cartão bruto 0–65535, **When** a grandeza física é 50 cm,
   **Then** o registrador de entrada carrega ~32767 (metade da escala bruta).
2. **Given** o mesmo sensor, **When** a grandeza física está no fundo de escala
   (0 cm) e depois no topo (100 cm), **Then** o registrador lê 0 e 65535
   respectivamente, sem estourar nem saturar antes do limite.
3. **Given** a cena rodando contra o OpenPLC v4, **When** o programa lê o `%IW`
   e aplica a fórmula de escala inversa, **Then** o valor em EU reconstruído
   bate com a grandeza física do sensor dentro da resolução de 16 bits.
4. **Given** dois sensores de nível iguais com cartões diferentes (faixas brutas
   distintas), **When** ambos leem a mesma grandeza física, **Then** cada
   registrador carrega a contagem bruta do seu próprio cartão — o escalonamento
   é por instância, não por tipo.

---

### User Story 2 - Comandar um atuador por setpoint (Priority: P2)

Quem programa CLP escreve um valor num registrador de saída (`%QW`) e um atuador
de velocidade variável obedece: a esteira anda mais rápido ou mais devagar em
proporção ao setpoint escrito. O operador vê, na tabela de I/O em modo manual,
que forçar um valor maior no `%QW` acelera o atuador.

**Why this priority**: é a metade de saída — fecha o par com a US1 e completa o
vocabulário (o CLP agora **escreve** número, não só lê). Depende do canal de
palavras existir (US1), por isso vem depois; mas é independentemente
demonstrável assim que existir.

**Independent Test**: montar uma cena com um atuador de velocidade variável,
forçar valores crescentes no registrador de saída via tabela de I/O em modo
manual, e assertar que a grandeza física do atuador (velocidade) escala
monotonicamente com o setpoint bruto segundo a faixa do cartão de saída.

**Acceptance Scenarios**:

1. **Given** uma esteira de velocidade variável mapeada num `%QW` com faixa de
   engenharia 0–2 m/s, **When** o CLP escreve o meio da escala bruta,
   **Then** a esteira anda a ~1 m/s.
2. **Given** a mesma esteira parada, **When** o setpoint bruto vai a 0,
   **Then** a esteira para; **When** vai ao fundo de escala, **Then** anda na
   velocidade máxima declarada.
3. **Given** a cena rodando contra o OpenPLC v4, **When** o programa escreve o
   `%QW` a cada tick, **Then** o valor lido de volta pela simulação reflete o
   que o master escreveu no holding register, sem defasagem de mais de um tick.

---

### User Story 3 - Controle de nível ponta a ponta (Priority: P3)

Quem programa CLP monta a cena de **controle de nível**: um sensor de nível
alimenta um `%IW`, um programa ST compara o nível lido contra um setpoint e
decide a velocidade de um atuador que escreve num `%QW`. A malha inteira roda
contra o OpenPLC v4 — entrada analógica, comparação/decisão sobre número, saída
analógica — a primeira lógica de CLP da Forja que age sobre um valor.

**Why this priority**: é a fatia que junta US1 + US2 numa aplicação reconhecível
de instrumentação. É o "cenário 06 do analógico": prova que o conceito fecha o
ciclo, não só que cada ponta funciona isolada. Vem por último porque depende das
duas metades.

**Independent Test**: montar a cena de controle de nível, escrever o programa ST
com setpoint e comparação, validar no OpenPLC v4 que abaixo do setpoint o
atuador acelera e acima ele desacelera (ou o comportamento de controle
especificado), e conferir determinismo pelo hash de estado ao longo de N ticks.

**Acceptance Scenarios**:

1. **Given** a cena de controle de nível rodando, **When** o nível está abaixo
   do setpoint, **Then** o programa comanda o atuador na direção que corrige o
   nível.
2. **Given** o nível cruzando o setpoint, **When** o programa reavalia a
   comparação, **Then** a saída analógica responde à mudança sem oscilar por
   ruído de quantização do próprio sinal bruto.
3. **Given** a mesma cena com o mesmo seed e a mesma sequência de entradas,
   **When** roda duas vezes por N ticks, **Then** o hash de estado final é
   idêntico nas duas execuções (Artigo I.4).

---

### User Story 4 - Carregar uma cena antiga sem trauma (Priority: P2)

Quem tem cenas digitais salvas na Fase 1 (schema v1) abre uma delas numa Forja
que já entende analógico (schema v2). A cena carrega inteira, sem ponto
analógico, com os campos novos preenchidos por padrão — nenhuma migração
destrutiva, nenhuma cena reescrita, nenhum erro.

**Why this priority**: a migração aditiva é o que garante que a Fase 2 não quebra
o que a Fase 1 entregou. É P2 porque, apesar de não ser a fatia "vendável", é um
critério de aceite duro do Artigo III e o molde para toda evolução de schema
futura. Testável assim que o `schemaVersion` subir.

**Independent Test**: pegar uma cena real da biblioteca salva em v1, carregá-la
numa Forja v2, e assertar que ela abre sem erro, roda idêntica à Fase 1, e os
campos analógicos novos aparecem com seus valores padrão.

**Acceptance Scenarios**:

1. **Given** uma cena `schemaVersion: 1` sem nenhum ponto analógico, **When**
   carregada numa Forja v2, **Then** abre sem erro e todo dispositivo digital
   se comporta exatamente como na Fase 1.
2. **Given** a mesma cena carregada em v2, **When** salva de volta, **Then** os
   campos aditivos aparecem com valores padrão explícitos e a cena continua
   válida.
3. **Given** uma cena v2 com um campo analógico malformado, **When** carregada,
   **Then** falha com erro explícito de migração (caminho + motivo), nunca
   corrompe silenciosamente (Artigo III.3, Artigo VII.3).

---

### Edge Cases

- **Grandeza física fora da faixa de engenharia do cartão** (nível acima de
  100 cm num cartão 0–100): o valor bruto satura no limite (0 ou 65535) em vez
  de estourar o `ushort` ou dar a volta (wrap-around).
- **Faixa de escala degenerada** (`euMin == euMax` ou `rawMin == rawMax`):
  erro de validação na carga da cena, não divisão por zero em runtime.
- **Direção × área × tipo incoerente** (um `Word` mapeado numa área de bit, ou
  um sensor mapeado numa área de saída): erro de validação (a regra que substitui
  o antigo `analog-not-supported`).
- **Conflito de endereço entre uma palavra e um bit** na mesma área/offset:
  erro de validação, como já é para bit×bit (Artigo VI.3).
- **Perda de conexão com o CLP no meio de uma malha analógica**: pausa a
  simulação e sinaliza; não continua com o último setpoint velho (Artigo VII.1).
- **Quantização**: um sinal em EU que não cai exatamente numa contagem inteira
  arredonda de forma determinística e documentada — o mesmo EU sempre vira a
  mesma contagem bruta.

## Requirements *(mandatory)*

### Functional Requirements

**Contrato e trânsito da palavra**

- **FR-001**: O contrato de I/O MUST carregar valores de 16 bits (palavras)
  paralelos aos bits, na mesma troca por tick, indexados por offset — sem um
  segundo canal de comunicação nem uma segunda passada.
- **FR-002**: Na direção de entrada, as palavras MUST corresponder aos
  registradores de entrada (sensor → CLP); na direção de saída, aos registradores
  de retenção (CLP → atuador). Um único formato serve às duas direções.
- **FR-003**: A palavra no fio MUST ser contagem bruta inteira de 16 bits. O
  valor em ponto flutuante de unidade de engenharia MUST NOT trafegar pelo
  contrato nem entrar no estado da simulação.

**Escalonamento (o cartão)**

- **FR-004**: Cada ponto analógico da cena MUST declarar sua faixa bruta
  (`rawMin`/`rawMax`, padrão 0–65535) — a propriedade do "cartão", por instância.
- **FR-005**: Cada dispositivo analógico MUST declarar sua faixa de engenharia
  (`euMin`/`euMax`) como parâmetro do tipo — a grandeza física que ele produz ou
  consome.
- **FR-006**: A fronteira de I/O MUST converter EU→bruto nas entradas e
  bruto→EU nas saídas, num único lugar, de forma determinística. Dois pontos com
  cartões diferentes lendo a mesma grandeza produzem contagens brutas diferentes.
- **FR-007**: A conversão MUST saturar nos limites da faixa em vez de estourar,
  dar wrap-around ou lançar exceção quando a grandeza sai da faixa do cartão.

**Catálogo e schema**

- **FR-008**: A definição de porta de um tipo de dispositivo MUST poder declarar
  o tipo de dado da porta (bit ou palavra); portas sem declaração explícita MUST
  continuar sendo bit (padrão), sem tocar nos dispositivos digitais existentes.
- **FR-009**: A versão de schema MUST subir de 1 para 2 de forma **aditiva**:
  uma cena v1 (sem ponto analógico) MUST carregar numa Forja v2 com os campos
  novos preenchidos por padrão, sem migração destrutiva.
- **FR-010**: Um campo analógico v2 malformado MUST falhar com erro explícito de
  migração (caminho + motivo), nunca corromper a cena em silêncio.

**Validação**

- **FR-011**: A validação MUST deixar de rejeitar pontos analógicos e passar a
  aceitar palavras mapeadas nas áreas de registrador corretas para sua direção.
- **FR-012**: A validação MUST rejeitar combinação incoerente de direção, área e
  tipo de dado (ex.: palavra numa área de bit, sensor numa área de saída) como
  erro, não warning.
- **FR-013**: A validação MUST tratar conflito de endereço envolvendo palavras
  com o mesmo rigor que já trata bits (Artigo VI.3).

**Determinismo**

- **FR-014**: O estado hasheado por tick MUST incluir as palavras em ordem
  estável de endereço, junto dos bits — duas execuções idênticas com um sinal
  analógico produzem hash idêntico (Artigo I.4).
- **FR-015**: O trânsito de palavras no hot-path MUST NOT alocar por tick de
  forma proporcional ao número de pontos (double-buffer reutilizado).

**Dispositivos**

- **FR-016**: A Forja MUST oferecer ao menos um sensor analógico de entrada (ex.:
  sensor de nível/distância) que publica uma grandeza física num registrador de
  entrada.
- **FR-017**: A Forja MUST oferecer ao menos um atuador analógico de saída (ex.:
  esteira de velocidade variável) que obedece a um setpoint lido de um
  registrador de retenção.
- **FR-018**: Cada dispositivo analógico novo MUST vir com catálogo em dados e
  ao menos um cenário headless que o valida ponta a ponta (Artigo V, Artigo VIII).

**UI**

- **FR-019**: A tabela de I/O MUST exibir pontos analógicos como número (com sua
  unidade de engenharia quando aplicável), não como aceso/apagado.
- **FR-020**: A tabela de I/O em modo manual MUST permitir forçar um valor
  numérico num ponto analógico.

### Key Entities *(include if data involved)*

- **Palavra de I/O**: valor bruto de 16 bits que trafega pelo contrato,
  indexado por offset dentro de sua área (registrador de entrada ou de retenção).
  Paralela ao bit; nunca contém unidade de engenharia.
- **Ponto analógico (cartão)**: descrição, na cena, de um canal analógico —
  qual porta de qual dispositivo, qual endereço de registrador, e a faixa bruta
  do cartão. Configurável por instância. É dado, versionado e inspecionável.
- **Faixa de engenharia**: par mínimo/máximo, declarado pelo tipo de dispositivo,
  que descreve a grandeza física (nível em cm, velocidade em m/s, peso em kg).
  Vive só na conversão; não trafega pelo fio.
- **Tipo de dado de porta**: atributo da definição de porta que distingue bit de
  palavra; padrão bit, para não tocar nos dispositivos digitais.
- **Dispositivo analógico**: sensor (produz grandeza física → registrador de
  entrada) ou atuador (registrador de retenção → grandeza física). Sensor de
  nível, balança e esteira de velocidade variável são os candidatos.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um sensor analógico publica um valor que um programa de CLP lê e
  reconstrói em unidade de engenharia dentro da resolução de 16 bits, validado
  contra o OpenPLC v4 real (US1).
- **SC-002**: Um atuador de velocidade variável obedece a um setpoint escrito
  pelo CLP, com a grandeza física escalando monotonicamente com o setpoint bruto,
  validado contra o OpenPLC v4 (US2).
- **SC-003**: A cena de controle de nível fecha a malha ponta a ponta (entrada
  analógica → comparação → saída analógica) contra o OpenPLC v4 (US3).
- **SC-004**: A mesma cena analógica, rodada duas vezes com o mesmo seed e as
  mesmas entradas por N ticks, produz hash de estado idêntico (determinismo).
- **SC-005**: Toda cena da biblioteca salva em schema v1 carrega numa Forja v2
  sem erro e roda idêntica à Fase 1 — zero cenas reescritas, zero regressões
  digitais.
- **SC-006**: 100% dos dispositivos analógicos novos têm ao menos um cenário
  headless que passa sem GPU e sem PLC real (Artigo V).
- **SC-007**: Nenhum dos 18 dispositivos digitais existentes precisa de
  alteração para continuar funcionando (prova da aditividade da mudança de porta).

## Assumptions

- **Faixas de exemplo**: sensor de nível 0–100 cm, esteira 0–2 m/s, cartão bruto
  0–65535. São padrões didáticos; a faixa real é configurável por instância e
  não altera o desenho.
- **Escalonamento linear**: a conversão EU↔bruto é linear (equivalente a um
  4-20 mA ideal). Linearização de sensor não-linear está fora de escopo.
- **Um valor por ponto**: cada ponto analógico é uma palavra de 16 bits única.
  Valores multi-registrador (32 bits, `float` no fio, ordem de bytes/word-swap)
  estão fora de escopo desta fase.
- **Sem alarme/limites automáticos**: comparação e setpoint vivem no programa de
  CLP (em ST), não em lógica embutida no dispositivo. A Forja fornece o número;
  o CLP decide.
- **OpenPLC v4** segue sendo o alvo de validação real, como nas Fases 0 e 1; o
  driver simulado (Artigo IV.4) cobre os testes headless.
- **Migração aditiva** assume que nenhum campo v1 muda de significado em v2 —
  apenas campos novos são acrescentados com padrões.

## Dependencies

- [ADR 0005](../../adr/0005-sinal-analogico-e-o-contrato-de-io.md) — decisões de
  arquitetura de camada 1 (contrato, escalonamento, schema, determinismo). A
  ratificar ao aceitar esta spec.
- [ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md) —
  critério de entrada de dispositivo novo (só entra quando habilita uma classe
  de lógica de CLP nova; analógico + setpoint é exatamente isso).
- [ADR 0002](../../adr/0002-ecossistema-de-custo-zero.md) — custo zero: OpenPLC,
  .NET e Godot já instalados.
- Constitution: Artigos I (determinismo), III (cena é dado / migração), V
  (headless), VI (I/O explícito), VII (falha segura), VIII (incremento vertical).
