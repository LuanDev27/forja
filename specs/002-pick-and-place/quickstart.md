# Quickstart — provando que o pick-and-place funciona

Roteiro de validação da spec 002. Cada bloco corresponde a uma história da
[spec](spec.md) e pode ser executado assim que aquela fatia estiver pronta —
não é preciso esperar a feature inteira.

## Pré-requisitos

Godot 4.4.1 mono e .NET SDK 8. Nesta máquina o SDK é user-local, então
`DOTNET_ROOT` precisa estar setado para o `dotnet` **e** para o Godot:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet build Forja.Studio.sln -c Debug
```

---

## V-A — A troca de tipo de corpo funciona (spike, R1)

**Prova o item de maior risco antes de qualquer outra coisa.** Se falhar aqui,
o desenho muda e nada mais vale.

```powershell
godot --headless --path . -- --forja-tests
```

**Esperado**: o cenário novo passa, demonstrando que uma peça pode virar
cinemática, ser reposicionada por N ticks, voltar a rígida e cair normalmente —
mantendo massa e atrito.

**Se falhar**: reabrir R1 no [research.md](research.md). A alternativa é recriar
o corpo, com o custo de id que o ADR 0004 documenta.

---

## V-B — Pegar, carregar e soltar (US1)

Aceite pelo **modo manual**, sem CLP externo.

```powershell
godot --path . -- --scene plc/06-pick-and-place/pick-and-place.forja
```

Na tabela de I/O, forçando as coils na ordem:

| # | Force | Observar |
|---|---|---|
| 1 | `lower` = 1 | o cabeçote desce; `lowered` vai a 1 |
| 2 | `grip` = 1 | `holding` vai a 1 e a peça para de cair |
| 3 | `lower` = 0 | a peça **sobe junto** com o cabeçote |
| 4 | `advance` = 1 | a peça atravessa para o destino |
| 5 | `lower` = 1 | desce sobre o destino |
| 6 | `grip` = 0 | `holding` cai e a peça é depositada |

**Esperado**: a peça sai de onde estava e fica no destino. Em nenhum momento ela
escorrega da garra ou é deixada para trás.

**Caso negativo obrigatório**: com o cabeçote no alto e nenhuma peça embaixo,
forçar `grip` = 1. `holding` deve permanecer **0** — a garra no vazio não prende
nada e não trava a sequência.

---

## V-C — Fins de curso permitem sequência sem cronômetro (US2)

Ainda em modo manual, observando a tabela de I/O:

**Esperado**:
- Durante o movimento de um eixo, `advanced` **e** `retracted` são ambos `0`.
- Ao chegar num extremo, só o daquele extremo vai a `1`.

Essa é a propriedade que permite escrever a sequência por confirmação em vez de
por tempo. Se `retracted` fosse simplesmente `NOT advanced`, ele estaria `1`
durante todo o avanço, e o programa avançaria de passo antes da hora.

---

## V-D — Ciclo completo comandado por CLP (US3)

Com o OpenPLC v4, seguindo
[`demo/openplc/README.md`](../../demo/openplc/README.md), carregando
`plc/06-pick-and-place/pick-and-place.st`.

**Esperado**: peças que chegam pela esteira são transferidas uma a uma ao
destino, em ciclo contínuo e sem intervenção.

**Verificar as três proibições** do
[contrato](contracts/pickplace-io.md#intertravamentos-que-o-contrato-torna-escrevíveis):
o eixo horizontal nunca se move com o vertical embaixo; a garra nunca solta fora
do destino.

---

## V-E — Determinismo (SC-002)

```powershell
godot --headless --path . -- --forja-tests
```

**Esperado**: o cenário de determinismo continua verde **com a unidade em
operação**. Duas execuções com a mesma semente produzem o mesmo hash, incluindo
qual peça foi agarrada.

É o teste que pega a falha mais provável desta feature: escolha não-determinística
de peça quando há mais de uma ao alcance (ver R3).

---

## V-F — Vinte transferências sem perder peça (SC-003)

Deixar o ciclo de V-D rodando por ao menos 20 transferências.

**Esperado**: nenhuma peça perdida, duplicada, presa indevidamente ou
esquecida no meio do caminho. A contagem que entra é a que sai.

---

## V-G — Orçamento de tick preservado (SC-004)

```powershell
godot --headless --path . -- --forja-tests
```

**Esperado**: o cenário de performance continua dentro do orçamento de 16,6 ms,
com folga comparável à atual (hoje p95 ≈ 4 ms com 200 peças).

---

## V-H — Voltar para Edit não deixa peça órfã (FR-009)

Com uma peça **presa na garra**, sair de Rodar para Editar.

**Esperado**: a peça volta a ser corpo normal. Ao voltar para Rodar, ela cai sob
gravidade como qualquer outra. Nenhuma peça fica flutuando nem presa a um
dispositivo que já foi desmontado.

---

## V-I — Falha de driver congela, não derruba (R4)

Com uma peça presa, derrubar o master Modbus.

**Esperado**: a simulação pausa e sinaliza `Driver: Erro` (Artigo VII.1). A peça
continua **presa e parada**, não cai. Ao reconectar e retomar, o ciclo continua
de onde estava.
