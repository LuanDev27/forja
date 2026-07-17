# Constitution — Projeto Forja

> *Forje a planta antes de construí-la.*

**Versão:** 1.0.0
**Ratificada em:** 2026-07-16
**Stack fixada:** Godot 4 (C#/.NET) · Jolt Physics · Windows 10/11 · Modbus TCP

## Nomenclatura dos Módulos

O nome mapeia diretamente nas camadas do Artigo II:

| Módulo | Camada | Papel |
|---|---|---|
| **Forja Studio** | 4 — Presentation | Editor de cena, UI, tabela de I/O |
| **Forja Core** | 2 — Simulation | Núcleo determinístico, física, dispositivos |
| **Forja Anvil** | 1 — Domain | Schema, contratos, tipos (a bigorna: base fixa) |
| **Forja Bellows** | 3 — IO | Drivers de PLC (o fole: sopra o ar de fora pra dentro) |

Namespaces C# seguem: `Forja.Anvil`, `Forja.Core`, `Forja.Bellows.Modbus`,
`Forja.Studio`. Extensão de arquivo de cena: **`.forja`** (JSON por dentro).

Este documento define princípios não-negociáveis. Qualquer plano (`plan.md`),
tarefa (`tasks.md`) ou PR que os viole deve ser rejeitado ou exigir emenda
formal a esta constitution.

---

## Artigo I — Determinismo da Simulação

1. A simulação roda em **fixed timestep** (`_PhysicsProcess`, 60 Hz). Nenhuma
   lógica de simulação pode depender de `_Process` ou de `delta` variável.
2. É **proibido** `Math.Random`, `GD.Randf` ou `System.Random` sem seed
   explícita dentro do núcleo de simulação. Toda aleatoriedade passa por um
   `IRandomSource` injetado e seedado por cena.
3. A ordem de iteração sobre entidades é **estável e definida** (ordem de
   inserção ou ID crescente), nunca `Dictionary` sem ordenação.
4. **Critério verificável:** rodar a mesma cena com o mesmo seed e a mesma
   sequência de inputs por N ticks produz hash de estado idêntico. Existe teste
   automatizado que prova isso.

## Artigo II — Separação de Camadas

Quatro camadas, com dependências apenas para baixo. Violação = build quebra.

```
┌──────────────────────────────────────┐
│ 4. Presentation (Godot nodes, UI)    │  ── pode ver 3, 2, 1
├──────────────────────────────────────┤
│ 3. IO Layer (drivers de PLC)         │  ── pode ver 2, 1
├──────────────────────────────────────┤
│ 2. Simulation Core (lógica, física)  │  ── pode ver 1
├──────────────────────────────────────┤
│ 1. Domain (schema, tipos, contratos) │  ── não vê ninguém
└──────────────────────────────────────┘
```

1. O **Simulation Core não referencia nós Godot de UI**, não lê input de
   teclado, não desenha nada.
2. A **camada de apresentação nunca muda estado de simulação diretamente** —
   apenas emite comandos que o core consome no próximo tick.
3. O core deve compilar e rodar em `godot --headless`. Se precisar de GPU para
   funcionar, o design está errado.

## Artigo III — Cena é Dado, Nunca Código

1. Toda cena de simulação é descrita por **JSON versionado** (`schemaVersion`),
   não por `.tscn` hardcoded nem por C# imperativo.
2. Adicionar um novo tipo de dispositivo (esteira, sensor, pistão) **não pode
   exigir recompilar** o editor. Registro de dispositivos é feito via catálogo
   de dados + factory.
3. Cenas antigas devem carregar em versões novas ou falhar com erro explícito
   de migração. Nunca corromper silenciosamente.

## Artigo IV — Driver de PLC é Plugin

1. O núcleo conhece apenas a interface `IPlcDriver`. Não conhece Modbus, S7 ou
   EtherNet/IP.
2. Trocar de protocolo é **configuração**, não recompilação do core.
3. A v1 entrega o driver **Modbus TCP**. Siemens S7 (S7NetPlus) e
   Allen-Bradley (libplctag.NET) entram depois **sem tocar** nas camadas 1 e 2.
4. Existe um **driver nulo/simulado** para rodar testes sem PLC real.

## Artigo V — Testabilidade Headless

1. Todo comportamento de simulação tem teste que roda **sem GPU e sem PLC**.
2. Teste de dispositivo tem a forma: monta cena mínima → roda N ticks →
   asserta estado. Não é teste de UI.
3. CI roda `godot --headless` + testes .NET. Build vermelho não faz merge.

## Artigo VI — Contrato de I/O Explícito

1. Todo sensor expõe **exatamente um** endereço de entrada. Todo atuador
   consome **exatamente um** endereço de saída. Sem mapeamento implícito.
2. O mapa de tags (`device.port → endereço Modbus`) é dado, salvo junto da
   cena, editável e inspecionável em runtime.
3. Conflito de endereço (dois dispositivos no mesmo bit) é **erro de
   validação**, não warning.

## Artigo VII — Falha Segura

1. Perda de conexão com o PLC **pausa a simulação** e sinaliza. Nunca continua
   com valores velhos silenciosamente.
2. Timeout de I/O é configurável e tem default explícito.
3. Erros de carregamento de cena são reportados ao usuário com caminho e
   motivo. Nunca `try { } catch { }` vazio.

## Artigo VIII — Incrementos Verticais

1. Cada tarefa entrega uma **fatia funcional demonstrável ponta a ponta**, não
   uma camada horizontal ("fiz toda a UI", "fiz todo o backend").
2. Toda tarefa tem critério de aceite verificável antes de ser escrita.

---

## Governança

- Emendas exigem: justificativa escrita, versão incrementada, e revisão dos
  `plan.md`/`tasks.md` afetados.
- Versionamento semântico: **MAJOR** = princípio removido/redefinido,
  **MINOR** = princípio adicionado, **PATCH** = clarificação.
- Em caso de conflito entre esta constitution e qualquer outro documento,
  **esta constitution prevalece**.

## Restrições Fixadas (não são princípios, são escolhas travadas)

| Item | Decisão | Motivo |
|---|---|---|
| Engine | Godot 4.4+ | Jolt nativo, editor 3D, export Windows trivial |
| Linguagem | C# / .NET | Ecossistema de drivers PLC (NModbus, S7NetPlus, libplctag) |
| Física | Jolt | Empilhamento e contato superiores ao Godot Physics |
| Plataforma | Windows 10/11 x64 | Requisito do usuário |
| Protocolo v1 | Modbus TCP | Denominador comum de todo PLC de mercado |
| Timestep | 60 Hz fixo | Determinismo |
