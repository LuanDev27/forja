# Forja

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
- **Catálogo de 17 dispositivos**: esteiras (fixa e acionada por I/O), emissor,
  removedor, sensores (fotoelétrico, proximidade capacitiva/indutiva, altura),
  atuadores (pistão, empurrador, trava), HMI (botão, chave, luz) e passivos
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

Baixe o ZIP, extraia e rode `Forja.exe`. Não precisa instalar nada — nem .NET.
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
dotnet test Forja.Studio.sln              # 110 testes de lógica (xUnit)
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

## Estado

v1 completa: as cinco fatias (esteira determinística → modo manual → editor →
catálogo → PLC real) estão entregues e aceitas. O caminho detalhado, com
tarefas e checklists de aceite, está em
[`specs/001-forja-v1/`](specs/001-forja-v1/).
