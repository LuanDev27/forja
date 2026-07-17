# Research — Forja v1 (Phase 0)

Todas as incertezas do Technical Context resolvidas. Formato:
Decisão / Justificativa / Alternativas consideradas.

---

## R1 — Versão do Godot e pin de determinismo

**Decisão:** Godot **4.4.x .NET** (travar na última patch estável 4.4 no
momento do setup; registrar a versão exata em `global.json`-like:
`.godot-version` + documentado no quickstart). Física **Jolt** selecionada em
`project.godot` (`physics/3d/physics_engine = "Jolt Physics"`), 60 ticks/s.

**Justificativa:** Jolt é módulo nativo a partir do Godot 4.4 (sem extensão
externa), com empilhamento/contato muito superiores ao Godot Physics — motivo
já travado na constitution. Pinar a versão exata mitiga o risco "determinismo
do Jolt sob carga" (spec §6): determinismo é garantido *no mesmo binário +
mesma plataforma*, que é o nosso caso (Windows x64 only).

**Alternativas:** Godot Physics (rejeitado: empilhamento instável); godot-jolt
extensão (desnecessário no 4.4+); Unity (rejeitado pela constitution).

## R2 — Biblioteca Modbus

**Decisão:** **NModbus** (pacote NuGet `NModbus`, o fork mantido da comunidade)
para o `ModbusTcpDriver`; o mesmo pacote fornece o master de loopback usado nos
testes de Bellows.

**Justificativa:** implementa master e slave TCP em .NET puro, sem
dependências nativas, licença MIT, API estável — cabe em Bellows sem
contaminar as camadas 1–2.

**Alternativas:** NModbus4 (abandonado); FluentModbus (bom, mas menos usado em
chão de fábrica); implementação própria (custo sem benefício na v1).

## R3 — Papel Modbus da v1: servidor **e** cliente (decisão do usuário 2026-07-16)

**Decisão:** a v1 entrega **dois drivers Modbus**, selecionáveis por
configuração (Artigo IV.2):

1. `modbus-tcp-server` — a Forja escuta em IP:porta (default `0.0.0.0:502`).
   O PLC master faz polling: lê **discrete inputs** (sensores) e escreve
   **coils** (atuadores). É o caminho documentado do OpenPLC (*Slave
   Devices*) e o usado pela cena demo (RF-09).
2. `modbus-tcp-client` — a Forja conecta **para fora** em `host:porta` de um
   servidor Modbus (PLC/gateway). Sensores são **escritos** pela Forja em
   coils/holding registers do PLC; atuadores são **lidos** de coils/holding
   registers. Os endereços de cada tag são livres — o usuário casa com o
   mapa do seu PLC. Necessário porque nem todo PLC suporta atuar como master.

**Justificativa:** o protocolo Modbus não permite que um cliente escreva
discrete inputs remotos, então o modo cliente usa coils/HR para os dois
sentidos — por isso o modo servidor continua sendo o default da demo OpenPLC.
Ambos os drivers ficam em Bellows atrás de `IPlcDriver`; core não muda
(Artigo IV.1). Status na UI: servidor = Aguardando master/Conectado; cliente
= Conectando/Conectado/Erro (RF-06 literal).

**Alternativas:** só servidor (rejeitado pelo usuário — quer cliente também);
um único driver com flag interna (rejeitado: ciclo de vida e semântica de
status muito diferentes; duas classes pequenas > uma classe com dois modos).

## R4 — Estratégia de testes em dois níveis

**Decisão:** (a) **xUnit** para tudo que é puro .NET: Anvil, lógica de Core
sem física (IoTable, StateHasher, migrações, máquina de modos), Bellows
(driver contra master NModbus em loopback na mesma máquina) e
**NetArchTest.Rules** para as regras de camada. (b) **GdUnit4 (C#)** rodando
via `godot --headless` para comportamento físico dos dispositivos e o teste de
determinismo E2E (10.000 ticks, hash).

**Justificativa:** satisfaz o Artigo V (sem GPU, sem PLC) e o CI da
constitution (`godot --headless` + testes .NET). GdUnit4 é o framework de
teste C# maduro para Godot 4 com runner headless e relatório JUnit para CI.

**Alternativas:** gdUnit3/WAT (Godot 3, obsoletos); só xUnit com física
mockada (não prova RF-04); runner custom (custo alto, reinventa GdUnit4).

## R5 — Hash de estado determinístico

**Decisão:** `StateHasher` FNV-1a 64-bit incremental sobre o estado *canônico*
por tick: para cada entidade em ordem de `EntityId` crescente — id, tipo,
estado discreto do dispositivo, e transform/velocidade das peças
**quantizados** (posição em mm inteiro, rotação em milésimos de radiano).
Valores de I/O entram no hash como bitmap ordenado por endereço.

**Justificativa:** quantizar remove ruído de representação float na
serialização do hash mantendo sensibilidade a divergência real; FNV-1a é
trivial, rápido e suficiente (não é criptográfico, é detector de divergência).
Ordem por EntityId cumpre o Artigo I.3.

**Alternativas:** xxHash64 (ok também; FNV escolhido por zero dependência);
hash de floats crus (frágil); serializar JSON e hashear (lento a 60 Hz).

## R6 — Persistência e migração do `.forja`

**Decisão:** `System.Text.Json` com `JsonSerializerOptions` estritos
(case-sensitive, sem campos desconhecidos ignorados silenciosamente em modo
validação), indentado (diffável). `schemaVersion` int no topo. Migrações como
cadeia `ISceneMigration (fromVersion → toVersion)` aplicadas em sequência;
versão futura ou gap na cadeia ⇒ erro explícito com caminho do arquivo e
motivo (Artigo III.3, VII.3).

**Alternativas:** Newtonsoft.Json (desnecessário no .NET 8); YAML (pior
tooling de schema em .NET); resource `.tres` do Godot (viola Artigo III.1).

## R7 — Undo/Redo do editor

**Decisão:** command pattern próprio em `Forja.Studio.Editor`: cada operação de
edição (`PlaceDevice`, `MoveDevice`, `DeleteSelection`, `EditParam`,
`ReassignAddress`, `DuplicateSelection`) implementa `IEditorCommand`
{`Do`, `Undo`} operando **sobre o SceneDocument** (dado, não sobre nós Godot);
pilha de 100 níveis (spec exige ≥ 50). A view Godot re-sincroniza do documento
após cada comando.

**Justificativa:** operar no documento (e não na cena Godot) mantém o Artigo
III (cena é dado) e faz undo/redo funcionar igual para qualquer dispositivo
futuro do catálogo. O `UndoRedo` do Godot é acoplado a Object/property e
ficaria preso à camada 4.

**Alternativas:** Godot `UndoRedo` (acoplamento errado); snapshots completos
do documento por operação (simples, mas 100 snapshots de cena grande custa
memória; comandos são baratos e precisos).

## R8 — Distribuição

**Decisão:** ZIP portátil (decisão Q4 aprovada): export Godot Windows x64 com
.NET embutido (self-contained), `export_presets.cfg` versionado, empacotado
com a cena demo e o programa OpenPLC de exemplo. Sem instalador na v1.

**Justificativa:** satisfaz RNF-06 (sem .NET pré-instalado) com o menor custo;
sem requisito de registro/atalhos que justifique MSI/Inno na v1.

**Alternativas:** Inno Setup (fica para v2 se houver demanda de instalação
"de verdade"); MSI/WiX (peso alto); MSIX (fricção de assinatura).

---

**Status:** nenhum NEEDS CLARIFICATION restante. Ponto ⚠ R3 é decisão tomada
e comunicada, não bloqueio.
