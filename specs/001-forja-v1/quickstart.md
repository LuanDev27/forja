# Quickstart — Forja v1 (guia de validação)

Como compilar, testar e validar o produto ponta a ponta. Não é documentação de
implementação — ver [plan.md](./plan.md) e contratos.

## Pré-requisitos

| Ferramenta | Versão | Nota |
|---|---|---|
| Godot .NET | 4.4.1 (pinada em `.godot-version`) | export templates só para empacotar |
| .NET SDK | 8.0.x | `dotnet --version` |
| OpenPLC Editor + Runtime | v4 | só para a validação RF-06/RF-09 (opcional nas demais) |

> Se o .NET for user-local (`%LOCALAPPDATA%\Microsoft\dotnet`), **DOTNET_ROOT
> precisa estar setado para o `dotnet` E para o `godot`** — sem isso o Godot
> segfalha ao carregar o runtime.

## Build

```powershell
dotnet build Forja.Studio.sln -c Debug   # compila as 4 camadas + testes
# Primeira importação de assets (uma vez, ou após mudar assets):
godot --headless --path . --import
```

> A solution se chama `Forja.Studio.sln` porque o GodotTools procura a
> solution pelo `assembly_name` do `project.godot` na hora do export.

## Testes (Artigo V — sem GPU, sem PLC)

```powershell
# 1. Testes .NET puros (Anvil, Core-lógica, Bellows, arquitetura)
dotnet test Forja.Studio.sln

# 2. Cenários com física real (runner próprio, decisão da sessão 1 — não GdUnit4)
godot --headless --path . -- --forja-tests
```

O runner roda os 15 cenários em sequência e sai com código 0/1 (pluga direto
no CI). Leva ~2 min: os últimos três são as medições de startup/carga,
performance com 200 peças e o soak de 30 min simulados.

Padrão de teste de dispositivo (Artigo V.2): montar `SceneDocument` mínimo em
código → deixar rodar N ticks → assert de estado (posição da peça, bit de I/O).
Nunca teste de UI. Os cenários vivem em `src/Forja.Studio/Headless/` porque
precisam do SceneTree do Godot; a lógica pura é testada em `tests/` com xUnit.

## Cenários de validação (mapeados aos aceites da spec)

### V-A — Determinismo (RNF-03, Artigo I.4)
1. Rodar `DeterminismScenario` (headless): mesma cena e semente, script de
   inputs fixo, duas execuções comparadas pelo hash de estado.
2. **Esperado:** hashes finais idênticos; divergência ⇒ falha com primeiro
   tick divergente.

### V-B — Física básica (RF-04)
1. Headless: caixa sobre esteira ligada → percorre e cai na calha.
2. 10 caixas em fila → nenhuma interpenetração (checagem de overlap).
3. Pistão empurra caixa parada → deslocamento sem atravessar.

### V-C — Modo manual, sem PLC (RF-07 + RF-01 + RF-05)
1. Abrir a Forja → carregar `demo/separador-altura.forja` → **Run**
   (driver `null`).
2. Na Tabela de I/O, forçar `%QX0.0` (esteira) = 1 → caixas andam.
3. Forçar `%QX0.1` (pistão) = 1 quando caixa L passa → caixa desviada.
4. Pause → Step → Step: avança exatamente 1 tick por clique.
5. **Esperado:** tudo operável sem PLC; Pause→Run sem salto de física.

### V-D — Endereço duplicado (RF-05, Artigo VI.3)
1. Em Edit, reatribuir o sensor de altura para `%IX0.1` (já usado pelo fim de
   curso).
2. Tentar Run.
3. **Esperado:** Run bloqueado; erro aponta os **dois** dispositivos.

### V-E — OpenPLC ponta a ponta (RF-06, RF-09)
1. Forja: cena demo, driver `modbus-tcp-server`, bind `0.0.0.0:5020`, Run
   (status: *Aguardando master*).
2. OpenPLC **v4**: programa ST + dispositivo remoto Modbus apontando para
   `127.0.0.1:5020`, conforme o passo a passo de
   [demo/openplc/README.md](../../demo/openplc/README.md) e os endereços de
   [contracts/modbus-mapping.md](./contracts/modbus-mapping.md); ▶ Play.
3. **Esperado:** status *Conectado*; sensor detecta caixa L ⇒ pistão avança em
   < 100 ms; caixas S seguem ao removedor.
4. Parar o OpenPLC no meio do Run → **Esperado:** Forja pausa e sinaliza
   (Artigo VII.1). Religar → reconecta e retoma.

> Porta 5020 (não a 502) para não exigir privilégio de administrador. No
> OpenPLC v4 o I/O remoto mapeia a partir de `%IX0.0`/`%QX0.0` — o `%IX100.x`
> era do v3. Resultado do aceite: [checklists/openplc-acceptance.md](./checklists/openplc-acceptance.md).

### V-F — Persistência round-trip (RF-08)
1. Montar cena nova no editor (≥ 3 dispositivos + mapa de I/O), salvar,
   fechar, reabrir.
2. **Esperado:** cena idêntica (posições, params, endereços, conexão).
   Teste automatizado: igualdade estrutural `load(save(doc)) == doc`.

### V-G — Editor do zero (RF-02)
1. Cena vazia → montar a demo do RF-09 só pelo editor (colocar, mover,
   rotacionar com snap, duplicar, undo/redo ≥ 50 níveis, deletar).
2. **Esperado:** sem editar JSON à mão; resultado passa V-C.

## Export (RNF-06, decisão Q4)

```powershell
pwsh build/package.ps1        # build Release + export + ZIP, num passo só
```

Gera `build/Forja-v1-win-x64.zip` (~65 MB) com o executável, o runtime .NET
embutido e — soltos ao lado do `.exe` — o catálogo e a demo. O catálogo fica
fora do `.pck` de propósito: o loader é da camada 1 (System.IO puro), que não
enxerga caminho dentro do pack.

**Esperado:** ZIP roda em Windows 10 21H2+/11 x64 limpo, sem .NET instalado;
abre em < 5 s (RNF-04). Verificação possível sem máquina limpa: rodar o pacote
com `DOTNET_ROOT` inválido e o `dotnet` fora do `PATH`.
