# Research — Pick-and-place

Decisões técnicas com risco, tomadas antes de escrever código. Cada uma diz o
que foi escolhido, por quê, e o que foi descartado.

As decisões de **arquitetura** (dispositivo composto, catálogo aberto, `SetKind`)
estão no [ADR 0004](../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md)
e não se repetem aqui.

---

## R1 — Trocar o tipo do corpo em tempo de execução

**Decisão**: `IPhysicsBody.SetKind(BodyKind)`, implementado com
`PhysicsServer3D.BodySetMode`.

**Fundamento**: `GodotPhysicsWorld.CreateBox` **já** chama `BodySetMode` na
criação, mapeando `BodyKind` para `PhysicsServer3D.BodyMode`. Chamar de novo é
uso previsto da API. O shape, o space e o id de entidade são configurados
separadamente (`BodyAddShape`, `BodySetSpace`) e não dependem do modo.

**Risco residual — deve ser provado antes de qualquer outra tarefa**: se a
troca de modo preserva massa e atrito, ou se o corpo precisa ser reconfigurado.
O plano trata isso como **spike da primeira fatia**, com cenário headless como
aceite, e não como detalhe de implementação.

**Alternativas descartadas**: recriar a peça (id novo quebra o hash de estado —
ver ADR 0004); junta física (complexidade desproporcional).

---

## R2 — Velocidade ao soltar: a física real resolve o requisito

**Decisão**: a peça é solta **em repouso**, a partir da pose corrente do
cabeçote. `IPhysicsBody` **não** ganha `SetLinearVelocity`.

**Fundamento**: um pick-and-place pneumático real **para antes de soltar** — o
eixo chega ao fim de curso, a garra abre, a peça cai. Soltar em movimento não é
o modo de operação; é um erro de programação que o próprio cenário 06 ensina a
evitar por intertravamento.

Manter a abstração mínima é o ganho: adicionar um setter de velocidade abriria
uma porta usada uma vez e disponível para sempre.

**Consequência assumida**: soltar no meio do curso faz a peça cair sem herdar a
inércia do cabeçote — fisicamente incorreto, mas só observável num comando que a
lógica correta nunca dá. Está registrado como caso de borda na spec, não como
comportamento suportado.

**Emenda proposta à spec**: a FR-006 diz "a partir da posição **e velocidade**
correntes do cabeçote". Deve passar a dizer apenas **posição**, com nota do
motivo. Se a implementação revelar que a queda em repouso produz artefato
visível (peça flutuando um instante), reabrir a decisão.

---

## R3 — Qual peça agarrar quando há mais de uma

**Decisão**: a de **menor `Id`** entre as que estão dentro do alcance.

**Fundamento**: `IPhysicsWorld.QueryBox` devolve `IReadOnlyList<uint>` **sem
garantia de ordem** — depende da ordem interna do broadphase, que pode variar
com a posição dos corpos. Escolher "a primeira da lista" quebraria o Artigo I.3
("ordem de iteração estável e definida") de um jeito que só apareceria
esporadicamente, que é o pior tipo de quebra de determinismo.

Id de peça é atribuído sequencialmente por `PartsManager` (`_nextId++`) e é
estável entre execuções com a mesma semente. Menor id = a peça mais antiga ao
alcance, que também é a escolha intuitivamente correta numa fila.

**Alternativa descartada**: a mais próxima do centro da garra. Mais "natural",
mas depende de distância em ponto flutuante — dois corpos equidistantes
empatariam e o desempate voltaria a depender da ordem do broadphase.

---

## R4 — Peça presa quando o driver cai

**Decisão**: nada de especial. A peça continua presa, congelada com o resto.

**Fundamento**: a falha de driver leva o loop a `SimMode.Pause`, que chama
`_physics.SetActive(false)`. A física inteira congela e o `Tick` dos
dispositivos não roda. A peça presa fica exatamente onde está, junto com todo o
resto da cena — que é precisamente o comportamento de falha segura do Artigo
VII.1: **nada se move, nada se perde, nada segue com valor velho**.

Soltar a peça na falha seria pior: ela cairia de onde estivesse, possivelmente
sobre a máquina, alterando o estado da planta durante uma falha.

**A ser garantido**: `Teardown` (saída para Edit) precisa desfazer o vínculo e
devolver a peça a corpo rígido. Sem isso, sobra peça cinemática flutuando ao
voltar para o editor. É a FR-009, e vira teste.

---

## R5 — Peça solta precisa ser acordada

**Decisão**: chamar `Wake()` na peça ao soltar.

**Fundamento**: `IPhysicsBody` documenta que a engine não acorda sozinha corpos
dormindo quando a superfície de contato muda. Uma peça que passou o transporte
como cinemática pode ser inserida como rígida já adormecida e **ficar parada no
ar**. O mesmo cuidado que a esteira já toma ao ligar.

---

## R6 — Colisão entre o cabeçote e a peça presa

**Decisão**: a peça presa vira **cinemática**, e o cabeçote também é cinemático.

**Fundamento**: dois corpos cinemáticos não resolvem contato entre si. Isso
elimina de graça o problema de a peça presa "brigar" com a garra que a segura —
que seria inevitável se ela continuasse rígida e sobreposta ao cabeçote.

**Consequência assumida**: a peça presa **atravessa** estrutura estática em vez
de colidir. É coerente com o resto da Forja (o pistão cinemático também atravessa
se comandado contra uma parede) e mantém o determinismo. O caso de borda da
spec pede apenas que seja observável e não corrompa a simulação — e é.

---

## R7 — Fins de curso: como derivar

**Decisão**: cada eixo expõe dois booleanos derivados da posição corrente,
com a mesma tolerância que o `Piston` já usa (`>= curso - 0,001`).

**Fundamento**: reusa exatamente o padrão de `Piston.Tick`, que já publica
`extended`. Não inventa conceito novo, e o programa de CLP enxerga a mesma
semântica que já aprendeu nos cenários 02 e 06.

`retracted`/`raised` são o espelho (`<= 0,001`), e não a negação de
`advanced`/`lowered` — no meio do curso **ambos são falsos**, que é a
informação que o programa precisa para saber que o eixo está em movimento.

---

## Itens sem incerteza

Registrados para não serem reabertos:

- **Registro do comportamento**: `factory.Register("pick-place", () => new PickPlace())`,
  uma linha, mesmo padrão dos treze existentes.
- **Hash de estado**: `StateHasher` já tem `AddQuantized(float)` e `Add(uint)` —
  suficiente para as duas extensões e o id da peça presa. Peça ausente entra
  como `0`.
- **Catálogo**: `DeviceTypeDef` já comporta N portas e N parâmetros. Nenhuma
  mudança de schema.
