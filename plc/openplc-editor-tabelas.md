# Carregar os cenários no OpenPLC Editor (STruC++) — tabelas de variáveis

> **A compilação não precisa mais de GUI.** Um comando compila todos os `.st`
> com o STruC++ de verdade, o mesmo do Editor instalado:
>
> ```powershell
> .\tools\openplc-validate\validar.ps1
> ```
>
> Estado atual: **8/8 compilam** (7 cenários + a demo do separador), incluindo o
> 07 com `%IW0`/`%QW0`. Ver [`tools/openplc-validate/`](../tools/openplc-validate/).

As tabelas abaixo continuam valendo para o que a linha de comando **não** faz:
rodar o programa no Runtime e ver a planta se mexer. Para isso é preciso montar
o projeto no Editor, e o caminho que funciona é **criar cada projeto ao vivo** —
`File → New Project` — preenchendo a tabela de variáveis à mão, porque o
`Open Project` do Editor 4.2.8 tem um bug que zera a tabela ao abrir do disco.

## Como montar cada cenário
1. **`File → New Project`** (linguagem ST, board **OpenPLC Simulator**). Nome livre.
2. Na **tabela de variáveis**, clique `+` e adicione cada linha abaixo
   (Class no dropdown; Location só nas Input/Output; Initial Value onde indicado).
3. **Corpo**: abra o `.st` correspondente, copie **tudo entre o último `END_VAR`
   e o `END_PROGRAM`**, cole na área de código.
4. **Build** — sem fechar/reabrir o projeto (o bug só ocorre ao reabrir).

**Constantes** (`ST_*`, `BATCH_SIZE`, `BUFFER_MAX`) → Class **Local**, Type
**INT**, o número no **Initial Value**. **Blocos funcionais** (`TON`, `R_TRIG`,
`CTU`, `CTUD`, `TP`) → Class **Local**, Type = nome do bloco, sem Location.
Locations válidas no Simulator: `%IX0.0–0.7`, `%QX0.0–0.7`.

| Cenário | Arquivo do corpo |
|---|---|
| 01 | `01-partida-parada-selo/partida-parada.st` |
| 02 | `02-intertravamento-emergencia/intertravamento.st` |
| 03 | `03-contagem-batelada/contagem.st` |
| 04 | `04-alarme-rearme/alarme.st` |
| 05 | `05-pulmao-semaforo/pulmao.st` |
| 06 | `06-pick-and-place/pick-and-place.st` |
| 07 | `07-controle-de-nivel/controle-nivel.st` |
| demo | `../demo/openplc/separador.st` |

---

## 01 — Partida/parada com selo
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| btn_start | Input | BOOL | %IX0.0 | |
| btn_stop | Input | BOOL | %IX0.1 | |
| motor | Output | BOOL | %QX0.0 | |
| lamp_run | Output | BOOL | %QX0.1 | |

## 02 — Intertravamento e emergência
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| btn_start | Input | BOOL | %IX0.0 | |
| btn_stop | Input | BOOL | %IX0.1 | |
| btn_emerg | Input | BOOL | %IX0.2 | |
| btn_reset | Input | BOOL | %IX0.3 | |
| sensor_part | Input | BOOL | %IX0.4 | |
| piston_extended | Input | BOOL | %IX0.5 | |
| belt_run | Output | BOOL | %QX0.0 | |
| piston_extend | Output | BOOL | %QX0.1 | |
| lamp_run | Output | BOOL | %QX0.2 | |
| lamp_emerg | Output | BOOL | %QX0.3 | |
| emergency | Local | BOOL | | TRUE |
| running | Local | BOOL | | FALSE |
| diverting | Local | BOOL | | FALSE |
| trig_part | Local | R_TRIG | | |
| trig_reset | Local | R_TRIG | | |
| retract_tmr | Local | TON | | |

## 03 — Contagem e batelada
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| BATCH_SIZE | Local | INT | | 5 |
| btn_start | Input | BOOL | %IX0.0 | |
| btn_stop | Input | BOOL | %IX0.1 | |
| btn_ack | Input | BOOL | %IX0.2 | |
| sensor_raw | Input | BOOL | %IX0.3 | |
| belt_run | Output | BOOL | %QX0.0 | |
| gate_close | Output | BOOL | %QX0.1 | |
| lamp_run | Output | BOOL | %QX0.2 | |
| lamp_batch | Output | BOOL | %QX0.3 | |
| running | Local | BOOL | | FALSE |
| debounce | Local | TON | | |
| trig_ack | Local | R_TRIG | | |
| counter | Local | CTU | | |
| part_seen | Local | BOOL | | |
| batch_done | Local | BOOL | | |

## 04 — Alarme com rearme
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| btn_start | Input | BOOL | %IX0.0 | |
| btn_stop | Input | BOOL | %IX0.1 | |
| btn_ack | Input | BOOL | %IX0.2 | |
| btn_reset | Input | BOOL | %IX0.3 | |
| btn_block | Input | BOOL | %IX0.4 | |
| sensor | Input | BOOL | %IX0.5 | |
| belt_run | Output | BOOL | %QX0.0 | |
| gate_close | Output | BOOL | %QX0.1 | |
| lamp_run | Output | BOOL | %QX0.2 | |
| lamp_alarm | Output | BOOL | %QX0.3 | |
| running | Local | BOOL | | FALSE |
| alarm_active | Local | BOOL | | FALSE |
| alarm_acked | Local | BOOL | | FALSE |
| jam_timer | Local | TON | | |
| trig_ack | Local | R_TRIG | | |
| trig_reset | Local | R_TRIG | | |
| t_off | Local | TON | | |
| t_on | Local | TON | | |
| blink | Local | BOOL | | |

## 05 — Pulmão com semáforo
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| BUFFER_MAX | Local | INT | | 5 |
| btn_start | Input | BOOL | %IX0.0 | |
| btn_stop | Input | BOOL | %IX0.1 | |
| s_entry | Input | BOOL | %IX0.2 | |
| s_waiting | Input | BOOL | %IX0.3 | |
| s_zone | Input | BOOL | %IX0.4 | |
| belt_run | Output | BOOL | %QX0.0 | |
| gate_close | Output | BOOL | %QX0.1 | |
| lamp_run | Output | BOOL | %QX0.2 | |
| lamp_full | Output | BOOL | %QX0.3 | |
| running | Local | BOOL | | FALSE |
| occupancy | Local | CTUD | | |
| zone_free_tmr | Local | TON | | |
| release | Local | TP | | |
| zone_free | Local | BOOL | | |
| release_req | Local | BOOL | | |
| buffer_full | Local | BOOL | | |

## 06 — Pick-and-place
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| ST_AGUARDA | Local | INT | | 0 |
| ST_DESCE | Local | INT | | 1 |
| ST_PEGA | Local | INT | | 2 |
| ST_SOBE | Local | INT | | 3 |
| ST_AVANCA | Local | INT | | 4 |
| ST_DESCE_DEP | Local | INT | | 5 |
| ST_SOLTA | Local | INT | | 6 |
| ST_SOBE_DEP | Local | INT | | 7 |
| ST_RECUA | Local | INT | | 8 |
| btn_start | Input | BOOL | %IX0.0 | |
| btn_stop | Input | BOOL | %IX0.1 | |
| s_peca | Input | BOOL | %IX0.2 | |
| advanced | Input | BOOL | %IX0.3 | |
| retracted | Input | BOOL | %IX0.4 | |
| lowered | Input | BOOL | %IX0.5 | |
| raised | Input | BOOL | %IX0.6 | |
| holding | Input | BOOL | %IX0.7 | |
| belt_run | Output | BOOL | %QX0.0 | |
| gate_close | Output | BOOL | %QX0.1 | |
| advance | Output | BOOL | %QX0.2 | |
| lower | Output | BOOL | %QX0.3 | |
| grip | Output | BOOL | %QX0.4 | |
| lamp_run | Output | BOOL | %QX0.5 | |
| running | Local | BOOL | | FALSE |
| passo | Local | INT | | 0 |

## 07 — Controle de nível
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| SP_NIVEL | Local | UINT | | 60 |
| BANDA | Local | UINT | | 5 |
| VEL_LENTA | Local | UINT | | 16384 |
| VEL_RAPIDA | Local | UINT | | 49151 |
| nivel_raw | Input | UINT | %IW0 | |
| vel_raw | Output | UINT | %QW0 | |
| nivel_pct | Local | UINT | | |
| drenando | Local | BOOL | | FALSE |

> **Este é o primeiro cenário com palavra, e ele compila.** O `validar.ps1`
> confirma no STruC++ 0.5.13: `UINT` é aceito, `DINT_TO_UINT(UINT_TO_DINT(...))`
> vira `TO_UINT(TO_DINT(NIVEL_RAW) * 100 / 65535)`, e os endereços entram como
> `LocatedArea::Input, LocatedSize::Word` e `LocatedArea::Output` — ou seja, a
> palavra foi ligada, não ignorada.
>
> O que ainda pode atrapalhar é o **board** no Editor: o *OpenPLC Simulator*
> expõe só `%IX0.0–0.7` e `%QX0.0–0.7` no dropdown de Location. Se ele recusar,
> escolha um board com áreas de registrador — o programa já está provado.

## demo — Separador por altura
| Name | Class | Type | Location | Initial |
|---|---|---|---|---|
| height_detect | Input | BOOL | %IX0.0 | |
| piston_extended | Input | BOOL | %IX0.1 | |
| start_button | Input | BOOL | %IX0.2 | |
| belt_run | Output | BOOL | %QX0.0 | |
| piston_extend | Output | BOOL | %QX0.1 | |
| run_light | Output | BOOL | %QX0.2 | |
| running | Local | BOOL | | FALSE |
| pushing | Local | BOOL | | FALSE |
| trig_start | Local | R_TRIG | | |
| trig_tall | Local | R_TRIG | | |
| retract_tmr | Local | TON | | |
