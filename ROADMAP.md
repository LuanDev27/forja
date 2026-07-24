# Roadmap — Forja e o ecossistema

> Este é o **plano**. As decisões que o sustentam estão em [`adr/`](adr/) —
> em especial o [ADR 0003](adr/0003-objetivo-e-empregabilidade.md), que define
> o critério usado para ordenar tudo aqui.

## Objetivo

Aprender e evoluir como programador ao longo de muitas sessões, e demonstrar
essa evolução para conseguir vaga de **dev júnior em automação industrial**.
Orçamento zero ([ADR 0002](adr/0002-ecossistema-de-custo-zero.md)).

Cada fase é avaliada por três eixos: **o que ensina**, **o que casa com vaga**,
**o que fica demonstrável**. Fase que não pontua em nenhum dos três não entra.

## Como este roadmap é organizado

Uma regra herdada da própria constitution (Artigo VIII — Incrementos
Verticais): **cada fase entrega uma fatia funcional demonstrável ponta a
ponta**, nunca uma camada horizontal. Nenhuma fase é "estudar X" — toda fase
termina em artefato que alguém pode abrir, rodar ou ler.

O detalhe diminui com a distância de propósito. As fases 0–3 estão descritas em
nível de tarefa, a 4 em nível de intenção, e depois dela o roadmap
deliberadamente para de planejar — pelos motivos em
[Depois da Fase 4](#depois-da-fase-4--ponto-de-decisão-não-fase-5). Planejar
hoje o último módulo da cadeia seria ficção: aquele plano só pode ser escrito
depois que os primeiros ensinarem o que ainda não sei.

## Estado atual

**Fases 0 e 1 fechadas. Fase 2 em progresso** — todo o código escrito e verde;
falta a validação no OpenPLC v4, que é passo de bancada.

- **Fase 0** — repositório público no GitHub, release `v1.0.0` (ZIP portátil),
  CI verde em `windows-latest`.
- **Fase 1** — biblioteca de **6 cenários de CLP** entregues (selo · intertravamento
  e emergência · contagem/batelada · pulmão com semáforo · alarme com rearme ·
  pick-and-place sequencial). Os `.st` validados **7/7 no MatIEC** e — nos cenários
  representativos (01, 02, 06) — também no **STruC++**, o toolchain do OpenPLC que
  roda em hardware.

**Spec 002 (pick-and-place v2)** fechada — 40/40. O pick-and-place ganhou física
de garra própria (`IPhysicsBody.SetKind`), geometria de gantry cartesiano e
cobertura headless das transições críticas (voltar para Edit e falha de driver,
ambas com peça presa na garra).

**Spec 003 (sinais analógicos)** — o cano de palavras está fiado ponta a ponta:
`IoSnapshot` carrega bits **e** palavras, a conversão bruto↔unidade de
engenharia mora só na fronteira `IoTable`, o schema subiu para v2 por migração
aditiva e o hash de determinismo passou a incluir as palavras. Três dispositivos
analógicos novos (sensor de nível, esteira de velocidade variável, balança) e o
**cenário 07 da biblioteca de CLP** — controle de nível com banda morta, a
primeira lógica da Forja que age sobre um número.

Números atuais: **21 dispositivos** no catálogo, **203 testes xUnit**, 7
cenários de CLP. Pendências da fase: validar `%IW`/`%QW` no OpenPLC v4 e ver a
Tabela de I/O analógica na tela — os dois passos que exigem bancada.

**Próximo passo:** fechar a Fase 2 na bancada (quickstart da spec 003) e então
decidir entre Fase 3 (gateway) e o visual industrial.

---

## Fase 0 — Tornar o portfólio existente público

**Por que primeiro:** o repositório não tem remote. Hoje o portfólio não
existe para ninguém além de você. É a maior diferença de valor pelo menor
esforço de todo este roadmap, e não depende de escrever uma linha de código.

**Entrega**
- Repositório **público** no GitHub. Público também remove o teto de CI:
  Actions é ilimitado em repo público, e o `ci.yml` roda em `windows-latest`,
  que consome cota em dobro — o [ADR 0002](adr/0002-ecossistema-de-custo-zero.md)
  registrou isso como a única pendência de custo em aberto.
- Release `v1.0.0` com o ZIP portátil (o `build/package.ps1` já gera).
- README já está pronto e com demo — ele é a porta de entrada, não o código.

**Ensina:** release, versionamento, CI em repositório público.
**Casa com vaga:** ter GitHub ativo é filtro de entrada, não diferencial.
**Custo:** zero. **Ordem de grandeza:** um dia.

---

## Fase 1 — De um programa de CLP para uma biblioteca

**Por que segundo:** é a maior lacuna do roteiro inteiro. O ecossistema como
planejado é quase todo engenharia de software, e vaga de automação júnior
pergunta *"você já programou CLP?"*. Um simulador que **conversa** com CLP é
adjacente; escrever a lógica é a coisa em si.

E o custo é baixíssimo porque a bancada já está montada: `demo/openplc/`
já tem `separador.st` rodando contra `separador-altura.forja`, com `R_TRIG`,
`TON`, máquina de estado e recolhimento seguro do pistão. Não é começar do
zero — é escalar o que já provou funcionar.

**Entrega:** uma biblioteca de cenários clássicos de automação. Cada um é um
trio: cena `.forja` + programa `.st` (ou ladder) + README curto explicando o
raciocínio e o que dá errado sem aquele intertravamento.

Candidatos, do mais simples ao mais completo:

| Cenário | O que exercita |
|---|---|
| Partida/parada com selo | lógica de selo, botoeira NA/NF |
| Intertravamento e emergência | parada segura, rearme obrigatório |
| Contagem e batelada | contadores, comparação, reset |
| Semáforo de esteira / pulmão | acumulação, liberação por espaço |
| Pick-and-place sequencial | sequenciamento por passos (SFC ou estado) |
| Alarme com rearme e sinaleiro | latch, reconhecimento, sinalização |

**Ensina:** IEC 61131-3 de verdade, e as manias da área — sempre rearmar,
nunca confiar em borda sem debounce, intertravar antes de acionar.
**Casa com vaga:** "programação de CLP", "ladder", "ST", "intertravamento" —
os termos mais citados em vaga júnior de automação.
**Custo:** zero (OpenPLC é livre). **Quase nenhum código novo na Forja.**

> Pode parecer que esta fase "não é programação" porque quase não mexe em C#.
> É o contrário: é a fase que converte o projeto de *"fiz um simulador"* para
> *"programo CLP e construí a planta onde testo"*. A segunda frase é a que
> abre porta em automação.

---

## Fase 2 — Forja v2: sinais analógicos

**Por que agora:** é o destravador do resto. A observação **O3** do
[ADR 0001](adr/0001-visao-de-integracao-ecossistema.md) já registrou que o
contrato é digital-only — `IoSnapshot` carrega `ReadOnlyMemory<bool>` e os
registradores existem só para não dar exceção ao master. Sem analógico,
qualquer SCADA ou dashboard adiante mostra só lâmpadas acesas e apagadas.

É também a mudança de maior custo entre as previsíveis, porque mexe num
contrato de **camada 1** e desce por Core, Bellows e mapa de tags — e é
justamente por isso que é o melhor exercício de arquitetura do projeto.

**Entrega** (spec 003 — o que já está verde)
- ✅ `IoSnapshot` com palavras além de bits; holding/input registers de verdade.
- ✅ Escalonamento de sinal (bruto ↔ unidade de engenharia), o equivalente a um
  4–20 mA, preso à fronteira `IoTable` por teste de arquitetura.
- ✅ Três dispositivos que só fazem sentido em analógico: sensor de nível,
  esteira de velocidade variável e balança.
- ✅ Migração de `schemaVersion` v1→v2 — cena da Fase 1 carrega com defaults
  explícitos e roda com o mesmo hash; campo analógico torto falha com caminho e
  motivo (Artigo III), sem corromper em silêncio.
- ✅ Cenário 07 da biblioteca: controle de nível ponta a ponta, com banda morta.
- ⏳ Validação na bancada: `%IW`/`%QW` contra o OpenPLC v4 e a Tabela de I/O
  analógica na tela.

**Ensina:** evoluir contrato versionado sem quebrar o passado; disciplina de
camada sob pressão real; migração de schema.
**Casa com vaga:** "sinal analógico", "escalonamento", "instrumentação",
"holding register".
**Custo:** zero.

---

## Fase 3 — Gateway: o ADR 0001 vira código

**Entrega:** o Gateway externo decidido no ADR 0001 — processo .NET separado,
cliente Modbus TCP de um lado, MQTT do outro (MQTTnet + Mosquitto self-hosted).

Duas coisas aqui não são detalhe:

- **Resolver a O1.** `MirrorDataStore.Touch()` usa um `_lastMasterActivity`
  global, sem distinguir conexões. Um Gateway sondando como segundo master
  mantém o timestamp fresco e **mascara a queda do CLP real**, anulando a falha
  segura do Artigo VII. É um problema de projeto de verdade, com mais de uma
  solução legítima — o melhor material de decisão técnica que o projeto vai
  oferecer, e vale um ADR próprio quando chegar.
- **Primeiro serviço .NET fora do Godot.** Muda o tipo de programa que você
  escreve: processo de longa duração, reconexão, log, configuração.

**Ensina:** MQTT (tópicos, QoS, retain), serviço de longa duração, integração
entre processos.
**Casa com vaga:** MQTT e IIoT aparecem cada vez mais em vaga de automação.
**Custo:** zero.

---

## Fase 4 — Supervisório (SCADA)

Aqui vale a regra do [ADR 0003](adr/0003-objetivo-e-empregabilidade.md):
**ferramenta padrão primeiro, implementação própria depois.**

1. **Ignition Maker Edition** (licença pessoal gratuita) ou **Elipse E3**,
   apontando para a Forja. Custa dias, dá o vocabulário da área — tag, alarme,
   tendência, historian — e põe no currículo um nome que aparece em vaga.
2. **Supervisório próprio** (ASP.NET Core + web) consumindo o MQTT da Fase 3.
   Custa semanas, e é o que diferencia de outro júnior que só configurou tela.

Fazer nessa ordem não é só sequência: o passo 1 te ensina o que o passo 2
precisa ter.

---

## Depois da Fase 4 — ponto de decisão, não "Fase 5"

Aqui o roadmap **para de planejar, de propósito.**

A cadeia original — SCADA → Industrial API → Gateway → IIoT → MES → Digital
Twin → Visão Computacional → SaaS — é uma escada de sofisticação de software, e
ela **se afasta do objetivo conforme sobe**. Levada até o fim, produz um dev
full-stack com uma história de automação no passado. O valor para vaga de
automação está concentrado nas fases 0–4, e a coisa de maior peso — programar
CLP — nem sequer estava na cadeia original.

Então a cadeia vale como **bússola, não como contrato**. Fases 0–4 são o
compromisso. O que vem depois é menu, escolhido pelo que servir na hora:

| Módulo | O que de fato entrega | Dito com honestidade |
|---|---|---|
| Industrial API + histórico | REST, modelagem, série temporal | mais perto de backend genérico do que de automação |
| MES / OEE | dado de chão de fábrica virando indicador de gestão | forte se a vaga for de integração/MES; é nicho |
| Digital Twin | reusa Godot para espelhar planta real | alto impacto visual em entrevista, baixo custo marginal |
| Visão computacional | OpenCV + ONNX inspecionando peças | é quase um projeto novo; só se virar interesse real |
| SaaS | empacotar como produto | fecha a narrativa, e é o mais distante do objetivo |

**Regra de entrada:** módulo novo só começa com a fase anterior **fechada e
demonstrável**. Nove começos valem menos que dois módulos terminados —
terminar é a habilidade sendo treinada aqui.

### O melhor desfecho é este roadmap ser interrompido

Se você for contratado durante a Fase 1 ou 2, o roadmap **terminou e deu
certo** — não foi abandonado. A partir daí quem te faz crescer é trabalho real,
com prazo, revisão de código e sistema de outra pessoa; a Forja vira projeto
paralelo ou portfólio congelado, e as duas coisas estão bem.

Isso fica escrito para não confundir sucesso com desistência quando acontecer.

---

## Anti-metas

O que **não** fazer, com o motivo:

- **Não reescrever a Forja v1.** Ela está entregue, testada e aceita. Reescrever
  parece progresso e não é — some do histórico e não gera artefato novo.
- **Não perseguir os nove módulos em paralelo.** Nove começos valem menos que
  dois módulos terminados. Terminar é a habilidade que se está treinando.
- **Não trocar de stack "para aprender mais".** C#/.NET atravessa todo o
  ecossistema e é forte no mercado de automação. Trocar reinicia a curva.
- **Não pular a Fase 1 por parecer pouco código.** É a fase de maior retorno
  para o objetivo declarado.
- **Não deixar fase fechar sem artefato demonstrável** — Artigo VIII.

## Como medir evolução

O critério não é "quantas fases fechei", é **o que ficou demonstrável**. Ao
fim de cada fase deve existir: algo que roda, e algo escrito explicando por
que foi feito assim.

A segunda parte é a subestimada. `specs/001-forja-v1/`, a constitution e os
ADRs deste repositório já mostram decisão registrada com alternativa rejeitada
e consequência assumida — júnior quase nunca mostra isso, e lê como sênior.
Manter esse hábito vale tanto quanto o código.
