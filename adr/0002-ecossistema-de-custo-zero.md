# ADR 0002 — Ecossistema de custo zero

**Status:** Aceito
**Data:** 2026-07-18
**Relacionado:** [ADR 0001](0001-visao-de-integracao-ecossistema.md) — este ADR
não revoga nada de lá; adiciona uma restrição que atravessa todos os módulos.
**Contexto de escopo:** documental. Nenhum código, nenhuma dependência.

## Contexto

O ecossistema (Forja → SCADA → Industrial API → Gateway → IIoT → MES → Digital
Twin → Visão Computacional → SaaS) é construído por uma pessoa, sem orçamento.
A restrição é: **desenvolver os nove módulos sem gastar dinheiro**.

O erro comum aqui é tratar isso como uma lista de compras — "para cada módulo,
escolher a opção grátis". Isso funciona por uns meses e quebra em dois pontos
previsíveis: quando um free tier muda de regra, e quando o projeto deixa de ser
pessoal. Este ADR trata custo zero como **restrição de arquitetura**, não como
critério de escolha de ferramenta.

## Decisão

### 1. Custo zero significa self-hostável em uma máquina

O ecossistema inteiro deve rodar em **um host** — o PC de desenvolvimento —
sem depender de nenhum serviço gerenciado externo. Nuvem, quando entrar, é
**demonstração opcional**, nunca dependência estrutural.

Consequência prática: se um módulo só funciona apontando para um serviço
gerenciado, o desenho está errado. Cada módulo se conecta a um endereço
configurável (broker, banco, API), e esse endereço tanto pode ser `localhost`
quanto uma URL remota.

Isso protege contra a falha real deste tipo de projeto: não é o preço, é a
**mudança unilateral de free tier**. Um free tier que encolhe vira um fim de
semana de migração; um `localhost` não muda de regra.

### 2. Licença permissiva por padrão

Ferramenta nova entra preferencialmente sob **MIT, Apache 2.0 ou BSD**.

Duas categorias são tratadas como **dívida, não economia**, e exigem decisão
consciente antes de entrar:

- **Copyleft de rede (AGPL-3.0)** — grátis, mas contamina: se o SaaS servir o
  software pela rede, o código do serviço precisa ser aberto, ou compra-se
  licença comercial.
- **"Free for non-commercial use"** — grátis exatamente até o projeto deixar
  de ser pessoal. É a que mais machuca, porque a fatura chega no melhor
  momento do projeto, não no pior.

A regra: **grátis para desenvolver ≠ grátis para comercializar**. Se a ponta
SaaS pode virar comercial, o custo zero só é real com licença permissiva.

### 3. Repositórios públicos — CI deixa de ter teto

GitHub Actions é ilimitado em repositório **público** e limitado (cota mensal)
em privado. O `ci.yml` da Forja roda em `windows-latest`, e runner Windows
consome cota em **dobro** — num repositório privado a cota se esgota rápido
com build Godot + testes headless.

Como o valor deste ecossistema é também de portfólio, público é a escolha
coerente: some o teto de CI e o código passa a ser o argumento.

### 4. Stack por módulo

| Módulo | Escolha custo zero | Licença | Observação |
|---|---|---|---|
| **Forja** | Godot 4 .NET, Jolt, NModbus, xUnit | MIT | já é custo zero — ver verificação |
| **Gateway** | .NET 8 + NModbus + MQTTnet | MIT | reaproveita a competência de C# |
| **Broker MQTT** | Mosquitto (ou EMQX OSS) | EPL/EDL | self-host, sem conta em lugar nenhum |
| **Industrial API** | ASP.NET Core | MIT | — |
| **Histórico** | PostgreSQL | PostgreSQL | TimescaleDB community é TSL, não OSI — ver A3 |
| **SCADA / IIoT** | dashboard próprio (ASP.NET + web) | seu | Grafana OSS é AGPL — ver A1 |
| **MES** | ASP.NET Core + PostgreSQL | MIT | — |
| **Digital Twin** | Godot (competência já existente) | MIT | reusa render e cena da Forja |
| **Visão Computacional** | OpenCV + ONNX Runtime | Apache 2.0 / MIT | modelo é a armadilha — ver A2 |
| **Treino de modelo** | Kaggle Notebooks / Colab (GPU grátis) | — | cota semanal; suficiente para fine-tune |
| **Hospedagem** | Oracle Cloud Always Free, ou Cloudflare | — | Vercel Hobby proíbe uso comercial — ver A4 |
| **CI** | GitHub Actions, repo público | — | ver decisão 3 |

Limites numéricos de free tier mudam com frequência e **não** foram verificados
nesta data — confirme antes de depender de qualquer um. O que este ADR fixa é
a *forma* (self-host primeiro, permissivo por padrão), que não depende deles.

## Armadilhas identificadas

As quatro que custam dinheiro de verdade, em ordem de risco:

**A1 — Grafana OSS é AGPL-3.0.** Self-hostado para uso próprio, tudo bem. Se o
SaaS embutir Grafana ou servir dashboards a partir dele, o copyleft de rede
entra em jogo. Como SCADA e IIoT são dois módulos do roteiro e ambos são
essencialmente dashboards, **construir o próprio** resolve a licença e entrega
muito mais valor de portfólio do que configurar Grafana. Recomendado.

**A2 — YOLO da Ultralytics é AGPL-3.0 com licença comercial paga.** É o
caminho default de quem começa visão computacional hoje, e é justamente o que
não serve para uma ponta SaaS comercial. Alternativas com licença permissiva:
**YOLOX** ou **RT-DETR** (Apache 2.0), ou treinar do zero em PyTorch (BSD).
Decidir isto **antes** de treinar — trocar de modelo depois significa refazer
treino, não trocar de import.

**A3 — TimescaleDB community usa a Timescale License (TSL), não OSI.** Proíbe
oferecer o próprio banco como serviço gerenciado. Para self-host e para um SaaS
que apenas *usa* o banco, não há problema. Se quiser evitar a discussão por
completo: PostgreSQL puro com particionamento por tempo aguenta bem o volume de
um simulador, ou InfluxDB OSS (MIT).

**A4 — Vercel Hobby é somente não-comercial.** Idem Netlify em partes, e idem
**Ignition Maker Edition** (o SCADA industrial "grátis": licença pessoal, uso
comercial proibido — tentador e caro). Para a ponta SaaS, hospedagem que
permite uso comercial no tier grátis: **Cloudflare Workers/Pages** e
**Oracle Cloud Always Free** (VM ARM que roda o stack inteiro, incluindo
broker e Postgres, sem prazo de expiração).

## Consequências

**Positivas**

- Nenhum módulo depende de conta em serviço externo para ser desenvolvido ou
  demonstrado. O ecossistema roda offline, num notebook, numa entrevista.
- A ponta SaaS pode virar comercial sem reescrita nem compra de licença.
- Um free tier que mude de regra vira inconveniente, não bloqueio.

**Negativas / custos aceitos**

- **Troca-se dinheiro por trabalho.** Construir o próprio dashboard em vez de
  subir um Grafana custa semanas. É deliberado: nesse projeto o trabalho é o
  produto (portfólio), então o gasto de tempo tem retorno duplo.
- Operação é por sua conta: backup, atualização, TLS, monitoramento. Serviço
  gerenciado resolveria isso por dinheiro.
- Self-host em um host só não tem alta disponibilidade. Aceitável — nenhum
  módulo aqui é sistema de missão crítica de terceiros.
- Modelos de visão com licença permissiva costumam ter acurácia de referência
  um pouco abaixo do YOLO mais recente. Aceitável para demonstração.

## Verificação: a Forja já é custo zero?

Checagem somente-leitura em 2026-07-18.

| Dependência | Origem | Custo |
|---|---|---|
| `GodotSharp` 4.4.1 | Godot Engine (MIT) | zero |
| `NModbus` 3.0.81 | OSS | zero |
| `xunit`, `xunit.runner.visualstudio` | OSS | zero |
| `NetArchTest.Rules` 1.3.2 | MIT | zero |
| `Microsoft.NET.Test.Sdk` | .NET SDK (MIT) | zero |
| Jolt Physics | via Godot (MIT) | zero |
| Windows 10/11 x64 | já licenciado | zero adicional |

**Conclusão: sim.** A Forja v1 não tem nenhuma dependência paga, nenhum SDK
sob licença restritiva e nenhuma chamada a serviço externo — ela roda sem rede,
sem conta e sem nuvem. Este ADR **não exige mudança alguma na Forja**.

Vale notar que isso não é sorte: é consequência do ADR 0001. Como a integração
mora num Gateway externo, a Forja não herda nenhuma dependência de broker, de
API ou de nuvem que os módulos de cima venham a ter. A decisão de custo zero e
a decisão de Gateway externo se reforçam.

### Ponto aberto

O repositório **ainda não tem remote** (`git remote -v` vazio). A escolha
público vs. privado na hora de publicar é o que decide se a CI tem teto — ver
decisão 3. É a única ação com efeito de custo pendente hoje.

## Alternativas consideradas

**Free tiers gerenciados como base (Supabase, Neon, HiveMQ Cloud, Railway).**
Menos trabalho de operação e ótimo para demonstrar rápido. Rejeitada como
*base*: cria dependência estrutural em regras que mudam sem aviso, e vários
pausam ou apagam projetos inativos — exatamente o que acontece com um portfólio
entre uma entrevista e outra. Continuam válidos como camada de demonstração
opcional sobre um stack self-hostável.

**Aceitar AGPL onde for mais rápido e resolver a licença depois.** Rejeitada
para visão computacional (A2), onde "depois" significa refazer treino. Aceitável
para ferramentas de operação que não são servidas ao usuário final.

**Crédito de nuvem de programa para estudante/startup.** Rejeitada como base:
tem prazo de validade. Serve para experimento pontual, não para fundação.
