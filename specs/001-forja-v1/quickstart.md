# Quickstart — Forja v1 (guia de validação)

Como compilar, testar e validar o produto ponta a ponta. Não é documentação de
implementação — ver [plan.md](./plan.md) e contratos.

## Pré-requisitos

| Ferramenta | Versão | Nota |
|---|---|---|
| Godot .NET | 4.4.x (pinada em `.godot-version`) | editor + export templates Windows |
| .NET SDK | 8.0.x | `dotnet --version` |
| OpenPLC Runtime | atual | só para a validação RF-06/RF-09 (opcional nas demais) |

## Build

```powershell
dotnet build Forja.sln -c Debug          # compila as 4 camadas + testes
# Primeira importação de assets (uma vez, ou após mudar assets):
godot --headless --import
```

## Testes (Artigo V — sem GPU, sem PLC)

```powershell
# 1. Testes .NET puros (Anvil, Core-lógica, Bellows, arquitetura)
dotnet test Forja.sln

# 2. Testes headless de física/dispositivos/determinismo (GdUnit4)
godot --headless --path . -s addons/gdUnit4/bin/GdUnit4CmdTool.gd -a tests/Forja.Headless.Tests
```

Padrão de teste de dispositivo (Artigo V.2): montar `SceneDocument` mínimo →
`SimulationLoop.Run(nTicks)` → assert de estado. Nunca teste de UI.

## Cenários de validação (mapeados aos aceites da spec)

### V-A — Determinismo (RNF-03, Artigo I.4)
1. Rodar `DeterminismTest` (headless): carrega `demo/separador-altura.forja`,
   seed 42, script de inputs fixo, 10.000 ticks, 2 execuções.
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
1. Forja: cena demo, driver `modbus-tcp`, bind `0.0.0.0:502`, Run
   (status: *Aguardando master*).
2. OpenPLC: carregar `demo/openplc/separador.st`; em *Slave Devices*,
   adicionar a Forja (IP da máquina, porta 502) conforme
   [contracts/modbus-mapping.md](./contracts/modbus-mapping.md); Start PLC.
3. **Esperado:** status *Conectado*; sensor detecta caixa L ⇒ pistão avança em
   < 100 ms; caixas S seguem ao removedor.
4. Parar o OpenPLC no meio do Run → **Esperado:** Forja pausa e sinaliza
   (Artigo VII.1). Religar → reconecta e retoma.

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
godot --headless --export-release "Windows Desktop" build/Forja.exe
Compress-Archive build/* Forja-v1-win-x64.zip
```

**Esperado:** ZIP roda em Windows 10 21H2+/11 x64 limpo, sem .NET instalado;
abre em < 5 s (RNF-04).
