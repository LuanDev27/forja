# Research — Fase 2: sinais analógicos

Fase 0 do [plan.md](plan.md). Todas as incógnitas de arquitetura foram fechadas
antes desta fase pelo [ADR 0005](../../adr/0005-sinal-analogico-e-o-contrato-de-io.md)
(ratificado 23/07). Aqui elas viram decisões técnicas concretas, ancoradas por
inspeção do código atual. Sem `NEEDS CLARIFICATION` pendente.

## R1 — Formato do canal de palavras no contrato

**Decisão**: `IoSnapshot` ganha `ReadOnlyMemory<ushort> Words` ao lado de
`ReadOnlyMemory<bool> Bits`, indexado por offset, na mesma troca por tick.
`Empty(tick)` passa a preencher os dois canais vazios.

**Rationale**: `IoSnapshot` (`IPlcDriver.cs:22`) é hoje
`(TickNumber, Bits, Valid)`. É um `readonly record struct` — acrescentar um
campo `ReadOnlyMemory<ushort>` com default `Empty` não quebra os call sites que
usam parâmetros nomeados, e o layout plano por offset (do qual o hot-path e o
hash dependem) se mantém. Simétrico às duas direções: entrada = input registers,
saída = holding registers, exatamente como `Bits` já cobre discrete inputs/coils.

**Alternativas rejeitadas**: dicionário device→registers (quebra o layout plano
por offset — ADR 0005, alternativa rejeitada); `float` no fio (viola "palavra é
16 bits porque Modbus é 16 bits" — a EU só existe na fronteira).

## R2 — Onde vive a conversão EU↔bruto

**Decisão**: na `IoTable`, na fronteira, num único método de escala. Entradas:
o dispositivo escreve EU (`SetInputWord(id, port, float eu)`), a IoTable converte
para `ushort` bruto usando a faixa do cartão do ponto. Saídas: a IoTable entrega
EU ao atuador (`GetOutputWord(id, port) → float`), convertendo do bruto lido do
driver. O registrador sempre carrega **bruto**; o programa de CLP escala de volta
em ST.

**Rationale**: mantém `float` e `ushort` num só lugar, sem vazar (ADR 0005,
consequência assumida). Espelha `SetInput`/`GetOutput` que já existem para bits
(`IoTable.cs:79,86`). A faixa bruta (`rawMin`/`rawMax`) é do **ponto da cena**
(por instância); a faixa de engenharia (`euMin`/`euMax`) é **parâmetro do tipo**
(`ParamDef` já suporta `Min`/`Max`/`Default` numérico — `DeviceTypeDef.cs:24`).

**Fórmula** (linear, saturando nos limites):
`raw = clamp(round( rawMin + (eu − euMin)·(rawMax − rawMin)/(euMax − euMin) ), rawMin, rawMax)`
e a inversa para saída. Arredondamento `MidpointRounding.ToEven` fixo → mesma EU
sempre vira a mesma contagem (determinismo, FR-014). `euMin==euMax` ou
`rawMin==rawMax` é erro de validação, não divisão por zero em runtime (edge case).

**Alternativas rejeitadas**: escala no device (ata faixa bruta ao tipo, não à
instância); escala só no CLP (esconde o conceito que a fase existe para ensinar).
Ambas no ADR 0005.

## R3 — Onde mora a faixa bruta do cartão na cena

**Decisão**: um campo de escala **opcional** no ponto de I/O. O `IoTag`
(`IoTypes.cs:49`) ganha um `AnalogScale? Scale` (record com `RawMin`/`RawMax`,
padrão `0..65535`), preenchido só quando a porta é `Word`. Tags de bit não têm
escala.

**Rationale**: mantém "cena é dado" (Artigo III) e configuração por instância
(dois sensores iguais, cartões diferentes — AS4 da US1). Como `IoTag` é um
`record` serializado por `System.Text.Json`, um campo anulável novo é aditivo:
cenas v1 (sem `scale`) desserializam com `null`. A EU (`euMin`/`euMax`) **não**
entra aqui — é do tipo, via `ParamDef`, para não duplicar por tag.

**Cuidado**: `SceneSerializer.Options` usa
`UnmappedMemberHandling.Disallow` (`SceneSerializer.cs:28`) — campo novo no
modelo é aceito na carga (default quando ausente), mas um campo **desconhecido**
no JSON falha. Isso é o que dá a falha explícita em campo v2 malformado (FR-010).

## R4 — Migração de schema 1→2 é aditiva, mas **precisa existir**

**Decisão**: registrar uma `ISceneMigration { FromVersion = 1 }` na
`SceneSerializer.Migrations` que carimba `schemaVersion: 2` sem remover nem
reescrever nada (aditiva). `CurrentSchemaVersion` vai a 2.

**Rationale**: **descoberta da inspeção** — o `SceneSerializer.Load`
(`SceneSerializer.cs:76`) roda `while (version < CurrentSchemaVersion)` e **falha
explicitamente** com "sem migração registrada de schemaVersion 1 → 2" se a cadeia
tiver buraco. Portanto "aditivo" não significa "sem migração": significa uma
migração **não-destrutiva** que só sobe a versão (os campos novos entram por
default do modelo na desserialização). Sem registrá-la, toda cena v1 pararia de
carregar — o oposto de FR-009.

**Alternativas rejeitadas**: migração destrutiva reescrevendo cenas v1
(desnecessária — ADR 0005); pular a migração e mudar só a constante (quebraria a
carga de toda cena v1, contra SC-005).

## R5 — Fiar os RegisterSource do MirrorDataStore

**Decisão**: espelhar o padrão dos bits.
- **Input registers** (sensor→master): `RegisterSource` vira double-buffer
  `ushort[]` com `Publish(ReadOnlyMemory<ushort>)` + troca de referência
  (`Interlocked.Exchange`), igual ao `InputSource` de bits (`MirrorDataStore.cs:55`).
  Novo `PublishInputWords(words)` chamado no `Exchange`.
- **Holding registers** (master→atuador): `RegisterSource` mantém um `ushort[]`
  escrito pela thread de rede (FC06/FC16) e um `CopyTo(ushort[])` chamado por
  tick, igual ao `CoilSource` (`MirrorDataStore.cs:92`). Novo `CopyHolding(dest)`.

**Rationale**: o andaime já existe — o `RegisterSource` atual (`:132`) só
tolera o master sondar. A thread de rede nunca bloqueia o tick (double-buffer nos
inputs, escrita atômica-o-suficiente por word nos holdings), preservando o
desacoplamento que o servidor já garante para bits. Não aloca por tick
proporcional a pontos (FR-015): os buffers são reusados.

**Exchange** (`ModbusTcpServerDriver.cs:129`) passa a publicar
`inputs.Words` além de `inputs.Bits`, e a devolver os holding registers copiados
no `Words` do snapshot de saída, ao lado das coils em `Bits`.

**Modo cliente** (`ModbusTcpClientDriver`): ler input/holding registers do master
remoto e escrever holding registers, espelhando o que já faz com discrete
inputs/coils. Regra de área do cliente (In pode cair em coil remoto) ganha o
análogo para registers na validação.

## R6 — PortType no catálogo sem tocar nos 18 digitais

**Decisão**: `PortDef` (`DeviceTypeDef.cs:21`) vira
`record PortDef(string PortName, IoDirection Direction, PortType Type = PortType.Bool)`.
Enum novo `PortType { Bool, Word }` em `Forja.Anvil.Scene` (ou `Catalog`).

**Rationale**: parâmetro com default `Bool` → os 18 dispositivos e todo JSON de
catálogo existente continuam válidos sem edição (SC-007). Dispositivos analógicos
declaram `Word`. A validação usa `port.Type` para a regra direção×área×tipo.

## R7 — Regra de validação que substitui `analog-not-supported`

**Decisão**: remover o bloco `analog-not-supported` (`IoMapValidator.cs:65`) e
estender a regra direção×área (`:76`) para uma matriz direção×área×tipo:

| PortType | Direction | Área válida (servidor) | Área válida (cliente) |
|---|---|---|---|
| Bool | In | DiscreteInput | DiscreteInput, Coil |
| Bool | Out | Coil | Coil |
| Word | In | InputRegister | InputRegister, HoldingRegister |
| Word | Out | HoldingRegister | HoldingRegister |

Erro `type-area-mismatch` (ou reusar `direction-mismatch`) para célula fora da
matriz; `duplicate-address` já cobre conflito com palavra (o `IoAddress` inclui
a área, então `%IW0` e `%QX0.0` não colidem, mas dois `%IW0` sim).

**Rationale**: fecha os edge cases "Word numa área de bit" e "sensor em área de
saída" como erro, não warning (FR-012, Artigo VI.3). A escala degenerada
(`euMin==euMax`/`rawMin==rawMax`) entra como validação nova `invalid-scale`.

## R8 — Dispositivos novos e seus cenários

**Decisão**: três comportamentos em `Forja.Core/Devices/`, cada um com catálogo
JSON e cenário headless (Artigo V), seguindo o molde de `PhotoSensor`
(`Sensors.cs`): `Tick` escreve/lê a IoTable, `WriteState` hasheia o estado.

- **Sensor de nível/distância** — grandeza física (altura acumulada / posição /
  raycast de distância) → `SetInputWord(id, "level", eu)`. Params `euMin`/`euMax`.
- **Balança** — soma o peso das peças sobre a plataforma (via `PartsManager` /
  física) → input register.
- **Esteira de velocidade variável** — lê `GetOutputWord(id, "speed") → float`
  como setpoint e aplica à velocidade da esteira (reusa a base de `Conveyors`).

**Critério de entrada** (ADR 0004): cada tipo habilita uma classe de lógica de
CLP nova — analógico + comparação + setpoint. Passa.

**P1/P2/P3**: US1 precisa só do sensor de nível; US2 só da esteira VV; US3 junta
os dois numa cena de controle de nível. A balança é o terceiro dispositivo que
prova que o canal de entrada de palavras é genérico, não amarrado a um sensor.

## Riscos confirmados (do rascunho, agora ancorados)

- **Determinismo do hash** (FR-014): o ponto mais fácil de quebrar em silêncio.
  Mitigação: teste de hash idêntico sobre uma cena analógica **antes** de subir
  a camada 2; arredondamento fixo `ToEven`.
- **Migração** (R4): a descoberta de que a migração precisa existir remove o
  risco de "cena v1 não carrega". Teste: carregar uma cena real da biblioteca.
- **Hot-path** (FR-015): buffers de palavra reusados, medido por ausência de
  alocação por tick (mesma disciplina dos bits).
