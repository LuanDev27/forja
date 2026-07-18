# Demo OpenPLC — Separador por altura (RF-09)

A Forja simula a planta; o **OpenPLC** executa a lógica de controle e conversa
com a Forja por **Modbus TCP** — a Forja é o *slave* (servidor, porta
**5020**) e o OpenPLC é o *master*.

```
OpenPLC Runtime ──FC02 lê sensores / FC15 escreve atuadores──▶ Forja:5020
```

> **Estas instruções são para o OpenPLC Runtime v4 + Editor novo** (os
> downloads atuais de autonomylogic.com). No v4 **não existe interface web**
> (`localhost:8080` era do v3) — tudo é feito pelo **OpenPLC Editor**, que
> compila o programa, configura o Modbus master e envia ao Runtime pela API
> local (porta 8443).

## 1. Instalar as duas partes

1. **OpenPLC Editor** (desktop) e **OpenPLC Runtime** (Windows), ambos em
   <https://autonomylogic.com/download/>.
2. Inicie o Runtime: menu Iniciar → **Start OpenPLC Runtime**. Deixe a
   janela de console aberta (é o serviço; ele escuta em `https://localhost:8443`
   — sem página web, é normal o navegador não mostrar nada útil).

## 2. Criar o projeto no Editor

1. **Novo projeto**, linguagem **ST** (Structured Text).
2. Na **tabela de variáveis** do programa, declare (coluna *Location* nas
   seis primeiras — são elas que casam com o dispositivo remoto do passo 3):

   | Nome | Classe | Tipo | Location |
   |---|---|---|---|
   | `height_detect` | Local | BOOL | `%IX0.0` |
   | `piston_extended` | Local | BOOL | `%IX0.1` |
   | `start_button` | Local | BOOL | `%IX0.2` |
   | `belt_run` | Local | BOOL | `%QX0.0` |
   | `piston_extend` | Local | BOOL | `%QX0.1` |
   | `run_light` | Local | BOOL | `%QX0.2` |
   | `running` | Local | BOOL | — |
   | `pushing` | Local | BOOL | — |
   | `trig_start` | Local | R_TRIG | — |
   | `trig_tall` | Local | R_TRIG | — |
   | `retract_tmr` | Local | TON | — |

   > **Atenção aos endereços:** o v3 mapeava I/O remoto a partir de
   > `%IX100.0`; o **v4 mapeia a partir de `%IX0.0`/`%QX0.0`** (o endereço
   > real aparece na coluna *Address* do IO Tag Mapping do passo 3 — use o
   > que o Editor mostrar lá).

3. No corpo do programa, cole a lógica (é o miolo de `separador.st`, que
   fica de referência ao lado deste README):

   ```iecst
   (* Start alterna liga/desliga na borda de subida. *)
   trig_start(CLK := start_button);
   IF trig_start.Q THEN
     running := NOT running;
   END_IF;

   (* Peça alta detectada => inicia o ciclo de desvio. *)
   trig_tall(CLK := height_detect);
   IF running AND trig_tall.Q THEN
     pushing := TRUE;
   END_IF;

   (* Pistão no fim de curso há 600 ms => peça desviada, recolhe. *)
   retract_tmr(IN := pushing AND piston_extended, PT := T#600ms);
   IF retract_tmr.Q THEN
     pushing := FALSE;
   END_IF;

   (* Desligar a planta no meio de um ciclo também recolhe o pistão. *)
   IF NOT running THEN
     pushing := FALSE;
   END_IF;

   piston_extend := pushing;
   belt_run := running AND NOT pushing;
   run_light := running;
   ```

## 3. Cadastrar a Forja como dispositivo remoto (Modbus master)

No botão **➕** no topo da barra lateral do projeto, escolha **Remote
Device** → nome `forja`, protocolo **Modbus** → **Create**. Na tela do
dispositivo:

- **Transport**: `TCP/IP` · **IP Address**: `127.0.0.1` · **Port**: `5020`
- **Timeout (ms)**: `1000` · **Slave ID**: `1`

Em **IO Tag Mapping**, clique **+** e crie os dois grupos (poll de `50 ms`
para reação < 100 ms):

| Name | Function Code | Cycle Time | Offset | Length |
|---|---|---|---|---|
| `sensores` | Read Discrete Inputs (FC 2) | 50 ms | 0 | 3 |
| `atuadores` | Write Multiple Coils (FC 15) | 50 ms | 0 | 3 |

Correspondência com a cena (contracts/modbus-mapping.md; endereços IEC
conforme a coluna *Address* do IO Tag Mapping):

| Forja | Endereço Forja | Variável no PLC |
|---|---|---|
| sensor de altura (detect) | DI 0 | `%IX0.0` |
| fim de curso do pistão (extended) | DI 1 | `%IX0.1` |
| botão Start (pressed) | DI 2 | `%IX0.2` |
| esteira principal (run) | coil 0 | `%QX0.0` |
| pistão desviador (extend) | coil 1 | `%QX0.1` |
| luz indicadora (on) | coil 2 | `%QX0.2` |

## 4. Enviar ao Runtime e rodar

1. **Forja primeiro**: rode o projeto no Godot, **Abrir…** →
   `demo/separador-altura.forja` → **Rodar**. O painel "Conexão PLC" mostra
   **Aguardando master…** (a Forja escuta na 5020).
2. No Editor, aba **Configuration** (Device → Configuration): IP
   `localhost` → **Connect** (na primeira vez crie o usuário/senha
   pedidos; o status vira `● Connected | PLC: EMPTY`).
3. Salve o projeto (**Ctrl+S**) e aperte o **▶ Play** na barra lateral —
   ele faz tudo: gera o ST, envia `program.zip` ao Runtime, acompanha a
   compilação e dá o Start no PLC. **O progresso aparece no painel
   Console** (baixo): `Program uploaded successfully` → `Compilation
   completed successfully` → `PLC started.`
4. Na Forja o estado muda para **Conectado**. No painel **HMI** (canto
   inferior esquerdo), pressione **Botão de comando #5** (Start):
   - a luz indicadora acende e a esteira liga;
   - peças baixas (S) seguem até o fim da esteira e caem na calha da direita;
   - peças altas (L) param sob o sensor, o pistão as desvia para a calha
     lateral e a esteira religa sozinha.
5. Start de novo desliga a planta.

## Falha segura (Artigo VII)

Com a planta rodando, dê **Stop PLC** no Editor (ou feche o Runtime): em
~1 s a Forja **pausa a simulação sozinha** e reporta "master sem atividade".
Religando o PLC, basta **Rodar** de novo na Forja.

## Solução de problemas

| Sintoma | Causa provável |
|---|---|
| `localhost:8080` não abre | Normal no v4 — não existe interface web; use o Editor |
| ▶ Play "não faz nada" | Veja o painel **Console** (o erro aparece lá); salve o projeto antes (Ctrl+S) |
| Programa roda mas nada responde | Locations das variáveis não batem com a coluna *Address* do IO Tag Mapping (v4 = `%IX0.x`, não `%IX100.x`) |
| Editor não encontra o Runtime | Janela "Start OpenPLC Runtime" fechada — abra pelo menu Iniciar |
| Estado fica em "Aguardando master…" | PLC sem *Start*, dispositivo remoto com host/porta errados, ou pontos de I/O não cadastrados |
| "Erro" ao dar Rodar na Forja | Porta 5020 ocupada — troque a porta no painel Conexão PLC (e no dispositivo remoto do Editor) |
| PLC conecta mas nada se move | Botão Start não pressionado (a esteira só roda com a planta ligada) |
| Firewall pergunta na primeira vez | Permita acesso em rede privada |
