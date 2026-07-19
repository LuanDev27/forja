# Forja

[![CI](https://github.com/LuanDev27/forja/actions/workflows/ci.yml/badge.svg)](https://github.com/LuanDev27/forja/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/LuanDev27/forja?label=download)](https://github.com/LuanDev27/forja/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> Forje a planta antes de construí-la.

Simulador 3D de plantas industriais para **testar lógica de PLC sem hardware**.
Você monta a esteira, os sensores e os pistões na tela, aponta o seu PLC para a
Forja por **Modbus TCP** e roda o programa de verdade contra uma planta que
obedece à física — antes de encostar num parafuso no chão de fábrica.

![A demo do separador por altura rodando: barra de modos, tabela de I/O com
os seis pontos em notação dupla, painel de conexão Modbus e o painel de HMI](docs/demo.png)

*A demo `separador-altura.forja` em modo Rodando, esperando o master Modbus.*

## O que a v1 faz

- **Editor de cena**: coloca, move (snap 0,1 m), gira (snap 15°), duplica,
  desfaz/refaz e salva a planta num arquivo `.forja` (JSON versionado).
- **Catálogo de 18 dispositivos**: esteiras (fixa e acionada por I/O), emissor,
  removedor, sensores (fotoelétrico, proximidade capacitiva/indutiva, altura),
  atuadores (pistão, empurrador, trava, pick-and-place de 2 eixos), HMI e passivos
  (piso, calha, guia lateral, grade).
- **Modo manual sem PLC**: tabela de I/O com forçar bit — dá para operar a
  planta inteira na mão antes de existir programa nenhum.
- **PLC de verdade**: servidor Modbus TCP (a Forja é o dispositivo remoto) ou
  cliente. Validado ponta a ponta com **OpenPLC v4**.
- **Determinismo**: mesma cena + mesma semente + mesmos inputs ⇒ mesmo hash de
  estado, tick a tick.
- **Falha segura**: PLC caiu ou parou de responder ⇒ a simulação **pausa
  sozinha** e diz por quê. Nunca segue com valores velhos.

## Rodando

### Do pacote pronto (usuário)

Baixe o ZIP em [**Releases**](../../releases), extraia e rode `Forja.exe`. Não
precisa instalar nada — nem .NET.
Depois: **Abrir…** → `demo\separador-altura.forja` → **Rodar**.

### Do código (desenvolvimento)

Pré-requisitos: [Godot 4.4.1 .NET](https://godotengine.org/download) (versão
exata em `.godot-version`) e .NET SDK 8.0.

```powershell
# .NET user-local? DOTNET_ROOT precisa estar setado para dotnet E godot.
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

dotnet build Forja.Studio.sln -c Debug
godot --headless --path . --import        # uma vez, após clonar
godot --path . -- --scene demo/separador-altura.forja
```

### Testes

```powershell
dotnet test Forja.Studio.sln              # 111 testes de lógica (xUnit)
godot --headless --path . -- --forja-tests  # 15 cenários com física real
```

Os cenários headless rodam a física Jolt de verdade, sem GPU e sem PLC —
incluindo determinismo, falha segura, performance com 200 peças e um soak de
30 minutos simulados. A suíte inteira leva ~2 min.

### Empacotar

```powershell
pwsh build/package.ps1        # gera build/Forja-v1-win-x64.zip
```

## Ligando um PLC

A demo `demo/separador-altura.forja` separa caixas altas das baixas: o sensor
de altura vê a peça alta, o PLC para a esteira e manda o pistão desviá-la.

O passo a passo com o OpenPLC v4 (projeto ST, dispositivo remoto Modbus,
transferência e teste de queda do PLC) está em
[`demo/openplc/README.md`](demo/openplc/README.md). O mapa de endereços é o de
[`contracts/modbus-mapping.md`](specs/001-forja-v1/contracts/modbus-mapping.md).

## Biblioteca de lógica de CLP

[`plc/`](plc/) tem cenários clássicos de automação, cada um com a planta, o
programa em Texto Estruturado e um README explicando o raciocínio — não só o
que a lógica faz, mas a decisão de projeto por trás dela e o que dá errado sem
aquele intertravamento.

| # | Cenário | Conceitos |
|---|---|---|
| [01](plc/01-partida-parada-selo/) | Partida/parada com selo | selo, parada dominante, botoeira NA e NF, nível vs borda |
| [02](plc/02-intertravamento-emergencia/) | Intertravamento e emergência | realimentação física, emergência travada, rearme obrigatório |
| [03](plc/03-contagem-batelada/) | Contagem e batelada | contador `CTU`, filtro de repique, ordem de avaliação, erro de um |
| [04](plc/04-alarme-rearme/) | Alarme com rearme e sinaleiro | latch de alarme, reconhecer ≠ rearmar, pisca vs fixo |
| [05](plc/05-pulmao-semaforo/) | Pulmão com liberação por espaço | `CTUD`, `TP`, singularização, ocupação deduzida |
| [06](plc/06-pick-and-place/) | Pick-and-place sequencial | máquina de estado por passos, intertravamento entre eixos |

É o que a bancada existe para fazer: em vez de descrever a lógica no papel, ela
roda contra uma planta que obedece à física, comandada por um CLP de verdade.

## Como o código é organizado

Quatro camadas, dependência só para baixo — regra vigiada por teste de
arquitetura que quebra o build se for violada:

| Camada | Projeto | Papel | Pode usar Godot? |
|---|---|---|---|
| 1 | `Forja.Anvil` | domínio puro: cena, catálogo, contratos, validação | não |
| 2 | `Forja.Core` | simulação: loop 60 Hz, física, dispositivos, I/O, edição | só a física, sem UI |
| 3 | `Forja.Bellows` | drivers de PLC (Modbus TCP, nulo) | não |
| 4 | `Forja.Studio` | Godot: render, editor, painéis, runner headless | é a camada dele |

Princípios que valem para qualquer mudança (detalhe em
[`.specify/memory/constitution.md`](.specify/memory/constitution.md)):

- **Tick único**: só `_PhysicsProcess` a 60 Hz avança a simulação.
- **Cena é dado**: nada de estado escondido em nó de cena; o `.forja` é a verdade.
- **UI é fina**: a interface enfileira comando e lê estado — nunca muta nada.
- **Driver é plugin**: o core não conhece Modbus.
- **Todo comportamento tem teste headless.**

## Integração futura

A Forja é o primeiro módulo de um ecossistema industrial maior (SCADA,
Industrial API, IIoT, MES, Digital Twin). O papel dela nesse conjunto é ser a
**fonte de dados do chão de fábrica**, exposta por Modbus TCP — nada mais.

A ponte para o resto (`Modbus TCP ↔ MQTT ↔ Industrial API`) será um **Gateway
externo**, projeto separado que ainda não existe. O core da Forja continua sem
conhecer MQTT ou HTTP, fiel ao princípio de que **driver é plugin**.

Nada disso está implementado — é intenção registrada, com a verificação de que
a arquitetura atual comporta a integração sem retrabalho, em
[`adr/0001-visao-de-integracao-ecossistema.md`](adr/0001-visao-de-integracao-ecossistema.md).

Todo o ecossistema é construído sob restrição de **custo zero**: self-hostável
numa máquina, licença permissiva por padrão, sem dependência de serviço
gerenciado. A Forja já cumpre isso — nenhuma dependência paga, roda sem rede e
sem conta. Detalhe e armadilhas de licença em
[`adr/0002-ecossistema-de-custo-zero.md`](adr/0002-ecossistema-de-custo-zero.md).

## Estado

v1 completa: as cinco fatias (esteira determinística → modo manual → editor →
catálogo → PLC real) estão entregues e aceitas. O caminho detalhado, com
tarefas e checklists de aceite, está em
[`specs/001-forja-v1/`](specs/001-forja-v1/).

O que vem depois — biblioteca de programas de CLP, sinais analógicos, Gateway e
supervisório — está em [`ROADMAP.md`](ROADMAP.md), e as decisões que o
sustentam em [`adr/`](adr/).

## Licença

[MIT](LICENSE) — use, modifique e distribua à vontade, inclusive
comercialmente, mantendo o aviso de copyright.

A Forja não depende de nada pago nem de serviço gerenciado: roda offline, sem
conta e sem chave de API. Godot (MIT), .NET (MIT) e Jolt (MIT) são todos
permissivos, então o pacote inteiro pode ser redistribuído sem contaminação de
licença — o critério está em
[`adr/0002-ecossistema-de-custo-zero.md`](adr/0002-ecossistema-de-custo-zero.md).
