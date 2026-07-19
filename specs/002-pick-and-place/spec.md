# Feature Specification: Pick-and-place

**Feature Branch**: `002-pick-and-place`

**Created**: 2026-07-19

**Status**: Draft

**Input**: User description: "Pick-and-place na Forja: dispositivo composto actuator.pickplace com dois eixos (horizontal e vertical) e garra por vácuo, capaz de pegar uma peça, carregá-la e soltá-la em outro lugar. Requer SetKind em IPhysicsBody para a peça alternar entre corpo rígido e cinemático. Decisões e alternativas rejeitadas já registradas no ADR 0004. O objetivo final é o cenário 06 da biblioteca plc/ com o SFC clássico de pick-and-place."

## Contexto

A Forja sabe **empurrar** (pistão), **segurar** (trava) e **apagar** (saída)
peças. Não sabe **prender**. Por isso a classe de lógica mais citada em vaga de
automação — sequenciamento por passos com intertravamento entre eixos — é hoje
inescrevível na bancada.

As decisões de projeto, com alternativas rejeitadas e consequências assumidas,
estão no [ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md).
Esta spec define **o que** precisa existir e como se prova que existe.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pegar, carregar e soltar uma peça (Priority: P1)

Quem monta a planta coloca uma unidade pick-and-place ao lado de uma esteira,
entra em modo Rodar e, **pela tabela de I/O em modo manual**, comanda a
sequência com a mão: desce, liga a garra, sobe, avança, desce, desliga a garra,
sobe, recua. A peça sai de onde estava e aparece no destino.

**Why this priority**: é a capacidade inteira. Sem ela não existe pick-and-place
— com ela, tudo o mais é conveniência. E como a Forja já tem modo manual, essa
fatia é demonstrável ponta a ponta sem escrever uma linha de programa de CLP.

**Independent Test**: abrir uma cena com o dispositivo e uma peça ao alcance,
forçar as saídas na tabela de I/O na ordem da sequência, e observar a peça ser
transportada e depositada. Entrega valor sozinha: já é um equipamento operável.

**Acceptance Scenarios**:

1. **Given** uma peça parada ao alcance da garra e o cabeçote recolhido e no
   alto, **When** o operador comanda descer e ligar a garra, **Then** a entrada
   de "peça presa" passa a indicar presença e a peça deixa de cair.
2. **Given** uma peça presa na garra, **When** o operador comanda subir e
   avançar, **Then** a peça acompanha o cabeçote sem escorregar nem atravessar
   estrutura.
3. **Given** uma peça presa sobre o ponto de destino, **When** o operador
   desliga a garra, **Then** a peça volta a cair sob gravidade a partir daquela
   posição.
4. **Given** a garra ligada **sem** peça ao alcance, **When** o operador
   observa a entrada de "peça presa", **Then** ela indica ausência — ligar a
   garra no vazio não trava a sequência.

---

### User Story 2 - Escrever a sequência num CLP de verdade (Priority: P2)

Quem programa CLP aponta o OpenPLC para a cena e escreve o SFC clássico de
pick-and-place, usando os fins de curso para saber quando cada passo terminou e
para intertravar os eixos entre si.

**Why this priority**: é o motivo de o dispositivo existir. Sem os fins de curso
a sequência só poderia ser escrita por tempo, que é exatamente o vício que a
biblioteca ensina a não ter (ver [cenário 02](../../plc/02-intertravamento-emergencia/):
intertravar contra realimentação física, não contra o comando).

**Independent Test**: com o dispositivo já funcionando (US1), escrever um
programa que avança um passo somente quando o fim de curso correspondente
confirma, e verificar que o ciclo completa sem depender de temporizador.

**Acceptance Scenarios**:

1. **Given** o cabeçote em movimento, **When** ele atinge um extremo,
   **Then** o fim de curso daquele extremo indica chegada e o do extremo oposto
   indica ausência.
2. **Given** um comando de avanço com o eixo vertical abaixado, **When** o
   programa intertrava contra o fim de curso de "no alto", **Then** o avanço
   não ocorre — o intertravamento é possível de escrever.

---

### User Story 3 - Cenário 06 da biblioteca (Priority: P3)

O trio final da Fase 1: cena `.forja`, programa `.st` e README explicando o
sequenciamento por passos, o intertravamento entre eixos e o que dá errado sem
cada um.

**Why this priority**: é o entregável que converte a capacidade em portfólio.
Depende inteiramente de US1 e US2 estarem prontas.

**Independent Test**: abrir a cena, carregar o programa no OpenPLC e ver o ciclo
completo rodar sozinho, repetidamente, sem intervenção.

**Acceptance Scenarios**:

1. **Given** a cena e o programa carregados, **When** o operador dá partida,
   **Then** peças que chegam pela esteira são transferidas uma a uma ao destino,
   em ciclo contínuo.

---

### User Story 4 - O equipamento parece um equipamento (Priority: P4)

O dispositivo é desenhado com geometria própria — coluna, travessa, cabeçote e
garra — em vez do desenho genérico de caixa.

**Why this priority**: coerência com o resto do catálogo, que já foi modelado.
Puramente visual: não altera comportamento nem I/O.

**Independent Test**: abrir a cena e comparar com os demais dispositivos; a
garra deve acompanhar visualmente os dois eixos.

**Acceptance Scenarios**:

1. **Given** o dispositivo em movimento, **When** os eixos se deslocam,
   **Then** o desenho acompanha a posição real usada pela física.

---

### Edge Cases

- **Garra ligada sem peça** — não prende nada e não trava a sequência (US1-4).
- **Mais de uma peça ao alcance** — prende exatamente uma, escolhida por
  critério determinístico e estável entre execuções.
- **Garra desligada em movimento** — a peça é solta e cai a partir dali,
  herdando a velocidade do cabeçote, sem "teletransporte" nem repouso instantâneo.
- **Peça presa quando a simulação sai de Rodar** (Pause/Edit) — o estado é
  desmontado sem deixar peça órfã presa a um dispositivo que não existe mais.
- **Peça presa atinge estrutura** — o comportamento é observável e não corrompe
  a simulação (a peça agarrada é conduzida, não empurrada por forças).
- **Duas execuções idênticas** — produzem o mesmo estado, incluindo qual peça
  foi agarrada e onde foi solta.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O catálogo MUST oferecer um tipo de dispositivo pick-and-place com
  dois eixos de movimento (um horizontal, um vertical) e uma garra.
- **FR-002**: O dispositivo MUST expor três saídas comandáveis pelo CLP: avançar
  o eixo horizontal, descer o eixo vertical, e acionar a garra.
- **FR-003**: O dispositivo MUST expor fins de curso como entradas para os dois
  extremos de cada eixo, permitindo ao programa saber a posição sem cronometrar.
- **FR-004**: O dispositivo MUST expor uma entrada indicando se há peça presa.
- **FR-005**: Com a garra acionada e uma peça ao alcance, o sistema MUST
  vincular essa peça ao cabeçote, de modo que ela acompanhe ambos os eixos e
  deixe de responder à gravidade.
- **FR-006**: Ao desacionar a garra, o sistema MUST devolver a peça ao
  comportamento normal a partir da posição e velocidade correntes do cabeçote.
- **FR-007**: Quando houver mais de uma peça ao alcance, o sistema MUST prender
  exatamente uma, por critério determinístico.
- **FR-008**: O estado do dispositivo — posição dos dois eixos e identidade da
  peça presa — MUST integrar o estado verificado de determinismo, de modo que
  divergência entre execuções seja detectada.
- **FR-009**: Ao sair do modo de execução, o sistema MUST desfazer qualquer
  vínculo garra-peça sem deixar peça em estado inconsistente.
- **FR-010**: O tempo de percurso de cada eixo MUST ser configurável por
  parâmetro, para a cena representar equipamentos de velocidades diferentes.
- **FR-011**: O curso de cada eixo MUST ser configurável por parâmetro.
- **FR-012**: A camada visual MUST desenhar o dispositivo com geometria própria,
  acompanhando a posição real dos eixos.
- **FR-013**: A biblioteca `plc/` MUST ganhar um cenário com cena, programa e
  README demonstrando o ciclo completo comandado por CLP.

### Key Entities

- **Unidade pick-and-place**: equipamento único com dois eixos e garra. Tem
  curso e velocidade por eixo, alcance de garra, e uma posição corrente por eixo.
- **Vínculo garra-peça**: relação temporária entre o dispositivo e uma peça,
  criada ao agarrar e desfeita ao soltar. Enquanto existe, a peça é conduzida
  pelo cabeçote em vez de pela física livre.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um ciclo completo de transferência (pegar numa posição, depositar
  noutra) pode ser comandado do CLP usando **somente** fins de curso como
  condição de avanço de passo, sem nenhum temporizador de percurso.
- **SC-002**: Duas execuções da mesma cena com a mesma semente produzem estado
  idêntico tick a tick, incluindo qual peça foi agarrada — verificável pela
  mesma comparação já usada nos cenários existentes.
- **SC-003**: Uma sequência de ao menos 20 transferências consecutivas completa
  sem peça perdida, presa indevidamente ou duplicada.
- **SC-004**: Com o dispositivo em operação contínua, o orçamento de tempo por
  tick de 60 Hz continua sendo respeitado com a mesma folga dos cenários atuais.
- **SC-005**: Quem nunca viu o projeto consegue, lendo apenas o README do
  cenário, entender por que cada intertravamento entre eixos existe e o que
  aconteceria sem ele.

## Assumptions

- **A garra é por posse, não por atrito.** A peça agarrada é conduzida pelo
  cabeçote; não se simula força de vácuo, escorregamento ou peso limite. É a
  abstração equivalente à do resto da Forja, onde a esteira move por velocidade
  de superfície em vez de por atrito de correia.
- **Dois eixos bastam.** Rotação da garra e um terceiro eixo ficam fora. O SFC
  clássico que a biblioteca precisa ensinar é de dois eixos.
- **A unidade é um dispositivo só**, e não uma montagem de dispositivos
  independentes — decidido no ADR 0004, sem alteração do formato de cena.
- **Peça é a peça existente** (caixa). Nenhum tipo novo de peça é introduzido.
- **Modo manual já existe** e serve como forma de aceite da US1, sem depender de
  CLP externo.
- **O cenário 06 usa OpenPLC v4**, como os cinco anteriores da biblioteca.
