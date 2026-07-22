(*
  Pick-and-place sequencial — cenário 06, o último da biblioteca.

  A planta: a esteira traz peças até uma trava. O sensor confirma peça em
  posição, e a unidade pick-and-place pega, transporta e deposita na caçamba
  ao lado. Depois volta e espera a próxima.

  O que este cenário ensina, e que nenhum dos cinco anteriores ensinou:
  MÁQUINA DE ESTADO POR PASSOS. Até aqui a lógica era combinacional com
  travas — cada saída era uma expressão do estado atual. Um ciclo sequencial
  não cabe nisso: a mesma condição física significa coisas diferentes
  dependendo de ONDE no ciclo se está.

  Regra que vale para o arquivo inteiro: NENHUM passo avança por tempo.
  Todo avanço espera a confirmação do fim de curso correspondente. Um
  programa escrito com temporizadores funciona na bancada e quebra no campo,
  quando a pressão de ar cai ou a peça é mais pesada.

  Endereços:
    Forja DI 0   -> %IX0.0  botoeira START
    Forja DI 1   -> %IX0.1  botoeira STOP
    Forja DI 2   -> %IX0.2  sensor: peça em posição de coleta
    Forja DI 3   -> %IX0.3  eixo horizontal AVANÇADO
    Forja DI 4   -> %IX0.4  eixo horizontal RECOLHIDO
    Forja DI 5   -> %IX0.5  eixo vertical EMBAIXO
    Forja DI 6   -> %IX0.6  eixo vertical NO ALTO
    Forja DI 7   -> %IX0.7  garra COM PEÇA
    Forja coil 0 -> %QX0.0  esteira
    Forja coil 1 -> %QX0.1  trava (fecha para segurar a peça na coleta)
    Forja coil 2 -> %QX0.2  avançar eixo horizontal
    Forja coil 3 -> %QX0.3  descer eixo vertical
    Forja coil 4 -> %QX0.4  acionar garra
    Forja coil 5 -> %QX0.5  sinaleiro de planta ligada
*)

PROGRAM pick_and_place
  VAR CONSTANT
    (* Passos do ciclo. Nomeados, e não 1..8 soltos: quem lê o código seis
       meses depois precisa saber o que o passo 5 significa. *)
    ST_AGUARDA   : INT := 0;   (* esperando peça chegar na coleta *)
    ST_DESCE     : INT := 1;
    ST_PEGA      : INT := 2;
    ST_SOBE      : INT := 3;
    ST_AVANCA    : INT := 4;
    ST_DESCE_DEP : INT := 5;
    ST_SOLTA     : INT := 6;
    ST_SOBE_DEP  : INT := 7;
    ST_RECUA     : INT := 8;
  END_VAR

  VAR
    btn_start AT %IX0.0 : BOOL;
    btn_stop  AT %IX0.1 : BOOL;
    s_peca    AT %IX0.2 : BOOL;
    advanced  AT %IX0.3 : BOOL;
    retracted AT %IX0.4 : BOOL;
    lowered   AT %IX0.5 : BOOL;
    raised    AT %IX0.6 : BOOL;
    holding   AT %IX0.7 : BOOL;

    belt_run   AT %QX0.0 : BOOL;
    gate_close AT %QX0.1 : BOOL;
    advance    AT %QX0.2 : BOOL;
    lower      AT %QX0.3 : BOOL;
    grip       AT %QX0.4 : BOOL;
    lamp_run   AT %QX0.5 : BOOL;
  END_VAR

  VAR
    running : BOOL := FALSE;
    passo   : INT  := 0;
  END_VAR

  (* ------------------------------------------------------------------
     1. SELO DE PARTIDA — cenário 01.

     Parar NÃO aborta o ciclo no meio. O passo corrente é preservado, e ao
     religar a máquina continua de onde estava. Abortar deixaria uma peça
     pendurada na garra a meio caminho — pior que continuar.
     ------------------------------------------------------------------ *)
  running := (btn_start OR running) AND NOT btn_stop;

  (* ------------------------------------------------------------------
     2. A MÁQUINA DE ESTADO

     Cada passo faz duas coisas e só duas: comanda as saídas daquele passo,
     e decide quando passar ao próximo. A condição de avanço é SEMPRE uma
     confirmação física.
     ------------------------------------------------------------------ *)
  IF running THEN
    (* Os rótulos abaixo são os literais inteiros dos passos, não os nomes
       ST_*: a IEC 61131-3 exige literal (ou faixa) como rótulo de CASE, e
       não constante nomeada. O nome de cada passo vai no comentário ao
       lado, e as constantes ST_* seguem nomeando tudo no resto da lógica
       (nos avanços `passo := ST_x` e nas saídas `passo = ST_x`). *)
    CASE passo OF

      (* Peça encostada na trava e sensor confirmando. Só então começa. *)
      0:  (* ST_AGUARDA *)
        IF s_peca AND raised AND retracted THEN
          passo := ST_DESCE;
        END_IF;

      1:  (* ST_DESCE *)
        IF lowered THEN
          passo := ST_PEGA;
        END_IF;

      (* Espera a garra CONFIRMAR que pegou. Sem isso o ciclo seguiria com a
         garra vazia e depositaria nada no destino — a falha mais comum de
         pick-and-place mal escrito. *)
      2:  (* ST_PEGA *)
        IF holding THEN
          passo := ST_SOBE;
        END_IF;

      3:  (* ST_SOBE *)
        IF raised THEN
          passo := ST_AVANCA;
        END_IF;

      4:  (* ST_AVANCA *)
        IF advanced THEN
          passo := ST_DESCE_DEP;
        END_IF;

      5:  (* ST_DESCE_DEP *)
        IF lowered THEN
          passo := ST_SOLTA;
        END_IF;

      (* Confirma a SOLTURA, não só o comando de soltar. *)
      6:  (* ST_SOLTA *)
        IF NOT holding THEN
          passo := ST_SOBE_DEP;
        END_IF;

      7:  (* ST_SOBE_DEP *)
        IF raised THEN
          passo := ST_RECUA;
        END_IF;

      8:  (* ST_RECUA *)
        IF retracted THEN
          passo := ST_AGUARDA;
        END_IF;

    END_CASE;
  END_IF;

  (* ------------------------------------------------------------------
     3. SAÍDAS DERIVADAS DO PASSO

     As saídas são função do passo, não espalhadas dentro do CASE. Assim
     existe UM lugar que responde "quando esta saída liga?", e é impossível
     duas partes do código disputarem a mesma bobina.
     ------------------------------------------------------------------ *)

  (* Descer nos dois pontos de parada — e SÓ neles. *)
  lower := running AND ((passo = ST_DESCE) OR (passo = ST_PEGA)
                     OR (passo = ST_DESCE_DEP) OR (passo = ST_SOLTA));

  (* INTERTRAVAMENTO 1: só avança com o eixo vertical NO ALTO.
     Sem o "AND raised", o carro varreria lateralmente na altura da esteira,
     derrubando a fila inteira. *)
  advance := running AND raised
             AND ((passo = ST_AVANCA) OR (passo = ST_DESCE_DEP)
               OR (passo = ST_SOLTA) OR (passo = ST_SOBE_DEP));

  (* INTERTRAVAMENTO 2: a garra segura do momento em que pega até o de
     soltar, e em nenhum outro. Ligar fora dessa janela pegaria peça em
     lugar errado; desligar dentro dela derrubaria a peça no caminho. *)
  grip := running AND ((passo = ST_PEGA) OR (passo = ST_SOBE)
                    OR (passo = ST_AVANCA) OR (passo = ST_DESCE_DEP));

  (* INTERTRAVAMENTO 3: a esteira só anda quando a célula está livre.
     Alimentar durante o ciclo empurraria a próxima peça contra a garra. *)
  belt_run := running AND (passo = ST_AGUARDA) AND NOT s_peca;

  (* A trava fica fechada: é ela que posiciona a peça na coleta. Fechada
     também é o estado seguro — programa parado não deixa a fila escorrer. *)
  gate_close := TRUE;

  lamp_run := running;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : pick_and_place;
  END_RESOURCE
END_CONFIGURATION
