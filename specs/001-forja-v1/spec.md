# Spec — Forja v1

> *Forje a planta antes de construí-la.*

**Status:** Draft para revisão
**Constitution:** 1.0.0
**Escopo:** o QUE o produto faz. Decisões de implementação vão em `plan.md`.

---

## 1. Problema

Programadores de PLC precisam validar lógica ladder/ST antes de ter a planta
física. Hoje: ou testam em hardware caro e indisponível, ou testam mentalmente
e descobrem os bugs no comissionamento — onde errar custa dinheiro e segurança.

Falta um simulador que (a) se comporte fisicamente de forma crível, (b) fale o
protocolo real do PLC, e (c) deixe o usuário **montar sua própria planta**, não
só rodar cenários prontos.

## 2. Usuários

| Persona | Necessidade primária |
|---|---|
| **Programador de PLC** | Testar lógica contra I/O realista sem hardware |
| **Instrutor / aluno** | Montar exercícios de automação reproduzíveis |
| **Integrador** | Validar sequência de máquina antes do comissionamento |

Fora de escopo v1: gêmeo digital de precisão para engenharia mecânica.

## 3. Requisitos Funcionais

### RF-01 — Modos de Operação
O sistema opera em quatro modos mutuamente exclusivos:
- **Edit** — montar/editar a cena. Física parada.
- **Run** — simulação rodando, I/O ativo.
- **Pause** — estado congelado, inspecionável.
- **Step** — avança exatamente 1 tick e volta a Pause.

**Aceite:** transição entre modos não corrompe estado; Pause→Run retoma sem
salto de física.

### RF-02 — Editor Genérico de Cena
O usuário monta uma planta posicionando dispositivos em espaço 3D.

Operações obrigatórias:
- Colocar dispositivo a partir de um **catálogo**
- Selecionar, mover, rotacionar (snap em grid + snap angular)
- Deletar
- **Undo/Redo** (mínimo 50 níveis)
- Salvar / Carregar cena
- Duplicar seleção

**Aceite:** montar a cena demo do RF-09 do zero usando apenas o editor, sem
editar JSON à mão.

### RF-03 — Catálogo de Dispositivos v1

**Passivos (sem I/O):**
- Piso / grade estrutural
- Calha estática (chute)
- Guia lateral

**Transportadores:**
- Esteira reta (velocidade configurável, bidirecional)
- Esteira acionada por I/O (liga/desliga por bit de saída)

**Sensores (→ 1 entrada digital):**
- Sensor fotoelétrico (barreira, feixe configurável)
- Sensor de proximidade capacitivo/indutivo (detecta por tipo de peça)
- Sensor de altura/difuso

**Atuadores (← 1 saída digital):**
- Pistão pneumático (extend/retract, curso configurável)
- Desviador (pusher)
- Stopper (trava de esteira)

**Fontes / Sumidouros:**
- Emissor de peças (tipo, intervalo, quantidade máx.)
- Removedor de peças (sink)

**Peças:**
- Caixa (tamanhos S/M/L), material metal/plástico

**HMI (v1 mínimo):**
- Botão push (→ entrada)
- Chave seletora (→ entrada)
- Luz indicadora (← saída)

**Aceite:** cada dispositivo do catálogo é colocável, salvável, recarregável e
tem pelo menos um teste headless.

### RF-04 — Comportamento Físico
- Peças têm massa, colidem, empilham e caem sob gravidade
- Esteira move peças em contato pela superfície (fricção tangencial)
- Peça que sai dos limites do mundo é destruída (sem vazamento de memória)

**Aceite:**
- Caixa colocada numa esteira ligada percorre-a e cai na calha
- 10 caixas em fila não se interpenetram
- Pistão empurra caixa parada sem atravessá-la

### RF-05 — Tabela de I/O
Painel que mostra, em tempo real:
- Cada dispositivo de I/O da cena
- Seu endereço mapeado (ex.: `%IX0.0` / coil 0)
- Valor atual (0/1)
- Direção (entrada/saída)

Operações:
- Reatribuir endereço
- **Forçar** valor manualmente (para teste sem PLC)
- Detectar e bloquear endereço duplicado

**Aceite:** endereço duplicado impede entrar em Run e mostra erro apontando os
dois dispositivos.

### RF-06 — Conexão com PLC (Modbus TCP)
- Conectar a um PLC/soft-PLC via IP:porta configuráveis
- Sensores escrevem em **discrete inputs / input registers**
- Atuadores leem de **coils / holding registers**
- Ciclo de I/O sincronizado com o tick de simulação
- Indicador de status: Desconectado / Conectando / Conectado / Erro

**Aceite:** com OpenPLC rodando lógica trivial (`sensor → atuador`), acionar o
sensor na simulação faz o pistão avançar em < 100 ms.

### RF-07 — Driver Nulo / Modo Manual
Rodar a simulação sem PLC, com I/O controlado manualmente pela tabela do RF-05.

**Aceite:** cena demo é operável ponta a ponta sem nenhum PLC conectado.

### RF-08 — Persistência
- Cena salva em **arquivo `.forja` (JSON versionado)**, legível e diffável
- Salva geometria + parâmetros + mapa de I/O + configuração de conexão
- Carregar cena de versão anterior: migra ou falha com mensagem clara

**Aceite:** salvar → fechar → abrir → cena idêntica (round-trip verificado por
teste).

### RF-09 — Cena Demo
Uma cena de exemplo entregue com o produto: **separador por altura**.

> Emissor solta caixas S e L numa esteira → sensor de altura detecta as L →
> pistão desvia as L para uma segunda calha → S seguem até o removedor.

**Aceite:** roda contra OpenPLC com programa ladder de exemplo incluído.

## 4. Requisitos Não-Funcionais

| ID | Requisito | Métrica |
|---|---|---|
| RNF-01 | Performance | ≥ 60 FPS com 200 peças ativas, i5 + GPU integrada |
| RNF-02 | Latência de I/O | < 20 ms do evento de sensor ao registro Modbus |
| RNF-03 | Determinismo | Mesmo seed + mesmos inputs → hash de estado idêntico em 10.000 ticks |
| RNF-04 | Startup | Aplicação abre em < 5 s |
| RNF-05 | Carga de cena | Cena de 100 dispositivos carrega em < 2 s |
| RNF-06 | Plataforma | Windows 10 21H2+ e Windows 11 x64. Instalador standalone, sem .NET pré-instalado |
| RNF-07 | Estabilidade | 8 h em Run contínuo sem crescimento de memória > 5% |

## 5. Fora de Escopo (v1 — explícito)

- Drivers S7 / EtherNet/IP (arquitetura pronta, implementação em v2)
- Multiplayer / colaboração
- Editor de ladder embutido (usuário traz seu PLC/soft-PLC)
- Linux / macOS
- Robôs articulados, AGVs, transportadores curvos
- Sensores analógicos (v1 é digital-only)
- VR
- Import de CAD

## 6. Riscos Conhecidos

| Risco | Severidade | Mitigação |
|---|---|---|
| Física de esteira exige tuning empírico longo | Alta | Fatiar cedo (T-02); aceitar iteração manual |
| Determinismo do Jolt sob carga | Média | Teste de hash desde T-01; travar versão do Godot |
| Empilhamento instável (jitter) | Média | Limitar peças ativas; sleep de rigidbody |
| Escopo do editor genérico inflar | Alta | Undo/redo e catálogo congelados no RF-02/RF-03 |

## 7. Perguntas Abertas — RESOLVIDAS (aprovadas pelo usuário em 2026-07-16)

1. **Unidade de mundo:** 1 unidade Godot = 1 m.
2. **Formato de endereço na UI:** notação IEC (`%IX0.0`) como exibição
   primária, com o endereço Modbus cru entre parênteses (ex.: `%IX0.0 (DI 0)`).
   Armazenamento canônico no `.forja` é o endereço Modbus cru.
3. **Emissor:** intervalo fixo apenas na v1 (trigger por entrada fica para v2 —
   mantém o catálogo congelado, Artigo VIII).
4. **Instalador:** ZIP portátil na v1 (export Godot self-contained, satisfaz
   RNF-06 sem .NET pré-instalado). Instalador Inno Setup pode entrar depois.

---

**Próximo passo:** aprovar este spec → `plan.md` (arquitetura de projeto,
solução .NET, schema JSON, interface `IPlcDriver`) → `tasks.md`.
