# ADR 0001 — Visão de integração do ecossistema

**Status:** Aceito
**Data:** 2026-07-18
**Contexto de escopo:** documental. Este ADR **não** implementa integração
alguma e não altera código de produção da Forja v1.

## Contexto

A Forja é o primeiro módulo de um ecossistema de software industrial maior. A
sequência pretendida é:

```
Forja (Simulador) → SCADA → Industrial API → Gateway → IIoT → MES
                  → Digital Twin → Visão Computacional → SaaS
```

Cada projeto alimenta o próximo. A Forja ocupa a base dessa pilha: ela é o
**chão de fábrica simulado** — a fonte de dados de processo que os demais
módulos consomem. Nenhum dos outros módulos existe hoje.

Isso cria uma tensão que este ADR resolve de forma explícita: sabemos que a
Forja vai precisar conversar com dashboards SCADA/IIoT e com um backend, mas
não sabemos ainda a forma exata desses consumidores. Implementar integração
agora seria antecipar requisitos de sistemas inexistentes e inflar o escopo da
v1.

## Decisão

### 1. A Forja permanece uma fonte de dados via Modbus TCP

O contrato de saída da Forja para o mundo externo continua sendo o que a v1 já
entrega: **Modbus TCP**, nos dois modos já suportados (servidor, em que a Forja
é o dispositivo remoto e o PLC é o master; e cliente). O mapa de tags
(`device.port → endereço Modbus`) é dado versionado junto da cena
(Artigo VI), portanto já é inspecionável e estável o suficiente para um
consumidor externo se apoiar nele.

Não adotamos nenhum protocolo adicional na Forja para viabilizar integração.

### 2. A integração é feita por um Gateway EXTERNO

A ponte `Modbus TCP ↔ MQTT ↔ Industrial API` será um **projeto separado**, um
processo próprio, com seu próprio ciclo de vida, deploy e versionamento. Ele
não é um módulo da Forja nem um driver dentro de `Forja.Bellows`.

```
┌──────────┐  Modbus TCP  ┌─────────┐  MQTT / HTTP  ┌──────────────┐
│  Forja   │◄────────────►│ Gateway │◄─────────────►│ SCADA / IIoT │
│ (planta) │              │(externo)│               │ Industrial   │
└──────────┘              └─────────┘               │     API      │
                                                    └──────────────┘
```

O Gateway fala Modbus TCP com a Forja e traduz para MQTT/HTTP do outro lado. A
Forja não sabe que ele existe.

### 3. O core da Forja nunca conhece MQTT nem HTTP

Corolário direto do **Artigo IV — Driver de PLC é Plugin** e do **Artigo II —
Separação de Camadas**:

- `Forja.Anvil` (camada 1) conhece apenas a interface `IPlcDriver`. Não conhece
  Modbus, muito menos MQTT ou HTTP.
- `Forja.Core` (camada 2) recebe um `Func<ConnectionConfig, IPlcDriver>`
  injetado. Não referencia `Forja.Bellows`.
- Nenhuma dependência nova (broker MQTT, cliente HTTP, serialização de
  telemetria) entra nas camadas 1 e 2 por causa de integração.

Se um dia a integração exigir algo dentro da Forja, o ponto de extensão correto
é um **driver novo em `Forja.Bellows`** (camada 3) selecionado por
configuração — nunca um vazamento de protocolo para dentro do core.

## Consequências

**Positivas**

- A v1 fecha sem carregar código especulativo. Não há integração a manter,
  testar ou versionar contra sistemas que não existem.
- O acoplamento entre Forja e ecossistema fica em um contrato já publicado e
  testado ponta a ponta (Modbus TCP, validado com OpenPLC v4), não em uma API
  interna instável.
- O Gateway pode evoluir, quebrar e ser reescrito sem tocar na Forja. O
  inverso também vale.
- A Forja continua rodando isolada, sem broker e sem rede além do PLC — o que
  preserva os testes headless (Artigo V) e o determinismo (Artigo I).

**Negativas / custos aceitos**

- Todo dado que o ecossistema quiser da Forja precisa existir como ponto de
  I/O no mapa Modbus. Estado interno da simulação que não vira tag é
  invisível para fora. Isso é uma limitação real e consciente.
- A v1 é **digital-only** (bits). Valores analógicos exigirão evolução de
  contrato — ver observação O3 abaixo.
- Haverá um salto de latência a mais no caminho até o dashboard (Forja →
  Gateway → broker → consumidor). Aceitável: dashboard não é malha de
  controle.

## Verificação: a arquitetura atual já suporta isso?

Checagem somente-leitura feita em 2026-07-18 sobre o repositório na v1.

| O que precisa ser verdade | Situação | Evidência |
|---|---|---|
| Modbus TCP exposto e isolado na camada 3 | ✅ | `NModbus` só aparece em `src/Forja.Bellows/Forja.Bellows.csproj`; `ModbusTcpServerDriver` / `ModbusTcpClientDriver` em `src/Forja.Bellows/Modbus/` |
| Driver é plugin, selecionado por configuração | ✅ | `IPlcDriver` em `src/Forja.Anvil/Contracts/IPlcDriver.cs:31`; resolução por chave em `src/Forja.Bellows/DriverRegistry.cs:16` |
| Core independente de protocolo | ✅ | `Forja.Core.csproj` referencia só `Forja.Anvil`; `SimulationLoop` recebe `Func<ConnectionConfig, IPlcDriver>` (`src/Forja.Core/Loop/SimulationLoop.cs:27`) |
| Core/Anvil independentes de UI | ✅ | `Forja.Anvil` sem nenhuma `ProjectReference`/`PackageReference`; regra vigiada por teste que quebra o build (`tests/Forja.Architecture.Tests/LayerRulesTests.cs`) |
| Composição de driver acontece só na camada 4 | ✅ | única chamada de produção a `DriverRegistry.Create` está em `src/Forja.Studio/Main.cs:90` |

**Conclusão:** a arquitetura atual suporta a integração futura via Gateway
externo sem retrabalho estrutural. Nenhuma camada precisa ser movida ou
reescrita.

### Observações (registradas, NÃO corrigidas nesta v1)

Pontos que merecem decisão antes de o Gateway existir. Nenhum deles é bug da
v1 nem bloqueia o fechamento do escopo atual.

**O1 — Um segundo master mascara a falha segura do primeiro.**
`MirrorDataStore.Touch()` (`src/Forja.Bellows/Modbus/MirrorDataStore.cs:42`)
atualiza um único `_lastMasterActivity` global, sem distinguir conexões. O
`ModbusTcpServerDriver` usa esse timestamp para decidir `Faulted`
(`ModbusTcpServerDriver.cs:148`). Se o Gateway se conectar como um segundo
master e ficar sondando, ele mantém o timestamp fresco — e a queda do PLC real
deixa de disparar a pausa exigida pelo **Artigo VII**. Um Gateway que consome
pelo modo servidor precisa ou de detecção de atividade por conexão, ou de um
caminho de leitura que não conte como atividade de master.

**O2 — Nada impede o Gateway de escrever.**
Um master Modbus tem FC05/FC15 disponíveis
(`MirrorDataStore.CoilSource.WritePoints`). Não há noção de consumidor
somente-leitura. Um Gateway de observabilidade que fale Modbus é, por
construção, capaz de comandar atuadores. Isso é uma decisão de segurança a
tomar do lado do Gateway ou por um modo read-only futuro na Forja.

**O3 — Contrato de I/O é digital-only.**
`IoSnapshot` carrega `ReadOnlyMemory<bool>` e o data-store trata registradores
como tolerados-mas-vazios (`MirrorDataStore.cs:30-34`). Dashboards SCADA/IIoT
tipicamente querem grandezas analógicas (velocidade de esteira, contagem,
temperatura). Estender isso mexe em um contrato de **camada 1**, com efeito
cascata em Core, Bellows e no mapa de tags. É a mudança de maior custo entre
as previsíveis — vale decidir cedo se a v2 a inclui.

**O4 — `DriverRegistry` é um `switch` fechado.**
`DriverRegistry.Create` (`src/Forja.Bellows/DriverRegistry.cs:17`) resolve
chaves conhecidas em tempo de compilação. "Driver é plugin" vale no nível da
interface (camadas 1 e 2 não mudam), mas acrescentar um driver ainda exige
recompilar `Forja.Bellows`. Coerente com o Artigo IV.2 como escrito
("trocar de protocolo é configuração, não recompilação **do core**"), e
suficiente para S7/EtherNet-IP. Só vira problema se o objetivo passar a ser
carregar drivers de terceiros sem rebuild.

**O5 — `Exchange` é síncrono dentro do tick.**
`SimulationLoop` chama `_driver.Exchange(inputs)` no caminho quente
(`src/Forja.Core/Loop/SimulationLoop.cs:315`), com orçamento de ~16,6 ms a
60 Hz. Isso reforça a decisão 2 deste ADR: qualquer coisa com latência de rede
não-determinística (broker MQTT, chamada HTTP) **não pode** ser implementada
como `IPlcDriver` in-band. O Gateway externo é a resposta certa, não um atalho.

## Alternativas consideradas

**Driver MQTT dentro de `Forja.Bellows`.** Manteria o core limpo, mas colocaria
latência de broker no caminho síncrono do tick (ver O5) e faria a Forja
depender de um broker para rodar. Rejeitada.

**Cliente HTTP publicando telemetria a partir do Studio.** Camada 4 pode fazer
isso sem violar o Artigo II, mas amarraria a Forja a um formato de API que
ainda não existe e criaria um segundo contrato externo para manter em paralelo
ao Modbus. Rejeitada por antecipação de requisito.

**Adiar a decisão e não escrever nada.** Rejeitada: a intenção de integração
já influencia como pensamos a v2 (ver O3). Registrar agora custa um arquivo e
evita que a v2 seja desenhada sem essa restrição em vista.

## Referências

- [`.specify/memory/constitution.md`](../.specify/memory/constitution.md) —
  Artigos II (camadas), IV (driver é plugin), VI (contrato de I/O), VII (falha
  segura)
- [`specs/001-forja-v1/contracts/iplcdriver.md`](../specs/001-forja-v1/contracts/iplcdriver.md)
- [`specs/001-forja-v1/contracts/modbus-mapping.md`](../specs/001-forja-v1/contracts/modbus-mapping.md)
- [ADR 0002](0002-ecossistema-de-custo-zero.md) — restrição de custo zero
  aplicada a todo o ecossistema; reforça a decisão 2 deste ADR (Gateway
  externo mantém a Forja livre de dependências de broker e nuvem)
