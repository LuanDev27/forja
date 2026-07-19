# ADR 0004 — Pick-and-place: abrir o catálogo congelado e ensinar a física a agarrar

**Status:** Aceito
**Data:** 2026-07-19
**Excetua:** T047 (catálogo v1 congelado em 17 tipos)
**Motiva:** `specs/002-pick-and-place`

## Contexto

A biblioteca de lógica de CLP ([Fase 1 do ROADMAP](../ROADMAP.md)) tem cinco
cenários entregues. O sexto previsto é **pick-and-place sequencial**, e é o
único que a Forja v1 **não comporta**.

O motivo não é falta de capricho: é ausência de capacidade. Um pick-and-place
pega uma peça, carrega e solta em outro lugar. A Forja sabe empurrar
(`Piston`), segurar (`Stopper`) e apagar (`sink`) — mas não sabe **prender**.

Duas lacunas concretas, encontradas por inspeção do código:

**L1 — a peça não pode mudar de natureza.** `BodyKind` é fixado em
`CreateBox` e `IPhysicsBody` não expõe como trocá-lo. Uma peça agarrada precisa
deixar de ser `Rigid` (sob gravidade) e virar `Kinematic` (acompanhando o
cabeçote), e voltar ao soltar.

**L2 — não existe cadeia cinemática.** Dispositivos têm pose absoluta na cena.
Um pick-and-place real é o eixo vertical montado sobre o horizontal, com a
garra na ponta.

A alternativa era adaptar: dois pistões em L simulando o movimento. Foi
rejeitada explicitamente — ver "Alternativas".

## Decisão

### 1. O catálogo deixa de ser congelado; passa a ser versionado

O congelamento do T047 em 17 tipos foi **controle de escopo da v1**, e cumpriu
o papel: impediu o catálogo de inflar enquanto as cinco fatias eram entregues.
Com a v1 aceita, ele deixa de proteger e passa a impedir.

O critério que substitui o congelamento:

> Tipo novo entra quando habilita uma **classe de lógica de CLP** que o
> catálogo atual não permite escrever. Não entra por variação estética,
> conveniência de cena ou completude de catálogo.

Pelo critério, `actuator.pickplace` entra: sequenciamento por passos com
intertravamento entre eixos é a classe mais citada em vaga de automação e é
inescrevível com os 17 tipos atuais. Um "pistão azul" não entraria.

### 2. `IPhysicsBody` ganha troca de tipo em tempo de execução

```csharp
void SetKind(BodyKind kind);
```

Resolve a L1. O custo é baixo porque o Godot já suporta: `GodotPhysicsWorld`
já chama `PhysicsServer3D.BodySetMode` na criação, e chamá-lo de novo é
legítimo. A física falsa dos testes de lógica acompanha.

É ampliação da abstração de camada 2, não vazamento: continua descrita em
termos de domínio (`BodyKind`), sem mencionar Godot.

### 3. Pick-and-place é um dispositivo COMPOSTO, não uma hierarquia

Rejeitamos adicionar `parentId` ao `DeviceInstance`. Em vez disso,
`actuator.pickplace` é **um** dispositivo que internamente possui um cabeçote
de dois eixos e uma garra.

Portas:

| Direção | Porta | Papel |
|---|---|---|
| Out | `advance` | eixo horizontal avança |
| Out | `lower` | eixo vertical desce |
| Out | `grip` | vácuo/garra fecha |
| In | `advanced` / `retracted` | fim de curso horizontal |
| In | `lowered` / `raised` | fim de curso vertical |
| In | `holding` | há peça presa na garra |

Isso mantém **cena é dado** (Artigo III) intacto: nenhuma mudança de
`schemaVersion`, nenhuma migração, nenhum conceito novo no editor.

E é fiel ao mundo real — uma unidade pick-and-place pneumática é vendida como
peça única, com um bloco de válvulas. Modelá-la como três dispositivos
independentes seria *menos* realista, não mais.

### 4. O estado agarrado entra no hash

As duas extensões e o id da peça presa entram em `WriteState`. Sem isso o
determinismo do Artigo V quebra em silêncio: duas execuções idênticas
divergiriam sem o hash acusar.

## Consequências

**Assumidas:**

- A Fase 1 do roadmap deixa de ser "quase nenhum código novo na Forja". Este
  cenário é trabalho de motor — parte da v2 puxada para frente,
  conscientemente, porque realismo foi priorizado sobre sequência.
- Mais um comportamento para manter, testar e desenhar.
- `SetKind` abre espaço para uso indevido (trocar tipo de corpo por conveniência
  em vez de por semântica). Mitigação: só o comportamento de garra usa.

**Ganhas:**

- A classe de lógica mais valiosa para vaga passa a ser escrevível.
- A garra é reutilizável: paletizador, transferência entre esteiras, robô
  cartesiano — tudo em cima da mesma capacidade.
- O critério de entrada de tipo fica escrito, então a próxima discussão de
  catálogo tem régua em vez de opinião.

## Alternativas rejeitadas

**Adaptar com dois pistões em L.** Ensinaria sequenciamento por passos com
custo quase zero. Rejeitada por decisão explícita de priorizar realismo: a
peça seria empurrada em dois tempos, não pega — e "pick-and-place que não
pega" é justamente o tipo de meia-verdade que não sobrevive a uma pergunta de
entrevistador.

**`parentId` no `DeviceInstance`.** Mais genérico e resolveria também
paletizador multi-eixo. Rejeitada por custo desproporcional agora: exige
`schemaVersion` novo, migração, e hierarquia no editor — muita superfície para
um caso. Continua sendo o caminho certo se um segundo equipamento multi-eixo
aparecer, e este ADR não fecha essa porta.

**Recriar a peça ao agarrar** (destruir o corpo rígido, criar um cinemático).
Evitaria mexer em `IPhysicsBody`. Rejeitada porque `PartsManager` atribui id
novo a cada `SpawnBox`, e id de peça entra no estado — trocar id ao agarrar
quebraria o determinismo de um jeito muito mais difícil de enxergar do que
adicionar um método à abstração.

**Junta/constraint física entre garra e peça.** É como se faz num motor de
física maduro. Rejeitada por complexidade desproporcional: a Forja não expõe
juntas, e agarrar por posse cinemática é suficiente e mais determinístico.
