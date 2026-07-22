(*
  Contagem e batelada — cenário 03 da biblioteca de lógica de CLP.

  A planta: esteira com uma TRAVA (stopper) segurando a fila, e um sensor
  fotoelétrico contando as peças que passam. A trava abre, as peças
  escoam, o sensor conta. Ao completar a batelada, a trava fecha e
  segura o resto até alguém reconhecer.

  Conceitos novos: contador CTU, constante de receita, filtro de
  repique (debounce) e — o mais escorregadio — a ORDEM em que o reset
  do contador é avaliado.

  Endereços:
    Forja DI 0   -> %IX0.0  botoeira START
    Forja DI 1   -> %IX0.1  botoeira STOP
    Forja DI 2   -> %IX0.2  botoeira RECONHECER (zera a batelada)
    Forja DI 3   -> %IX0.3  sensor fotoelétrico (conta peça)
    Forja coil 0 -> %QX0.0  esteira (run)
    Forja coil 1 -> %QX0.1  trava (close = TRUE segura a fila)
    Forja coil 2 -> %QX0.2  sinaleiro VERDE — planta ligada
    Forja coil 3 -> %QX0.3  sinaleiro AMARELO — batelada completa
*)

PROGRAM contagem
  VAR CONSTANT
    (* A "receita": quantas peças formam uma batelada. Fica como
       constante nomeada, e não como 5 solto no meio da lógica, porque
       esse é o número que muda quando o cliente muda de pedido. *)
    BATCH_SIZE : INT := 5;
  END_VAR

  VAR
    btn_start  AT %IX0.0 : BOOL;
    btn_stop   AT %IX0.1 : BOOL;
    btn_ack    AT %IX0.2 : BOOL;
    sensor_raw AT %IX0.3 : BOOL;

    belt_run   AT %QX0.0 : BOOL;
    gate_close AT %QX0.1 : BOOL;
    lamp_run   AT %QX0.2 : BOOL;
    lamp_batch AT %QX0.3 : BOOL;
  END_VAR

  VAR
    running : BOOL := FALSE;

    debounce   : TON;      (* filtro de repique do sensor *)
    trig_ack   : R_TRIG;   (* reconhecimento por borda *)
    counter    : CTU;      (* o contador de peças *)

    part_seen  : BOOL;
    batch_done : BOOL;
  END_VAR

  (* ------------------------------------------------------------------
     1. SELO DE PARTIDA — cenário 01.
     ------------------------------------------------------------------ *)
  running := (btn_start OR running) AND NOT btn_stop;

  (* ------------------------------------------------------------------
     2. FILTRO DE REPIQUE

     O sinal só é considerado "peça presente" depois de 20 ms estável.
     Sensor de verdade repica: peça com furo, superfície brilhante,
     vibração da estrutura — qualquer um desses gera pulsos curtos que
     o contador leria como peças a mais.

     20 ms é curto o bastante para não perder peça (a 0,5 m/s uma peça
     de 12 cm leva 240 ms cruzando o feixe) e longo o bastante para
     matar repique.
     ------------------------------------------------------------------ *)
  debounce(IN := sensor_raw, PT := T#20ms);
  part_seen := debounce.Q;

  (* ------------------------------------------------------------------
     3. RECONHECIMENTO — por BORDA, e ANTES do contador.

     Duas coisas aqui, e as duas custam caro se erradas:

     BORDA: se o reset fosse por nível, segurar a botoeira manteria o
     contador preso em zero e a batelada nunca completaria.

     ORDEM: em ST as linhas rodam de cima para baixo. O R_TRIG precisa
     ser avaliado ANTES da chamada do contador, senão o pulso de reset
     só chegaria no ciclo seguinte — e nesse ciclo intermediário o
     contador ainda estaria completo, reabrindo a trava por 50 ms.
     ------------------------------------------------------------------ *)
  trig_ack(CLK := btn_ack);

  (* ------------------------------------------------------------------
     4. O CONTADOR

     CTU é o contador crescente da IEC 61131-3:
       CU : conta +1 a cada BORDA DE SUBIDA deste sinal
       R  : zera CV
       PV : valor de comparação (a meta)
       CV : valor atual
       Q  : TRUE quando CV >= PV

     Note que CU é sensível a BORDA por dentro do bloco. É por isso que
     dá para ligar o sensor direto: a peça fica ~240 ms na frente do
     feixe, mas conta uma vez só. Se CU fosse por nível, a 50 ms de scan
     uma única peça viraria cinco.
     ------------------------------------------------------------------ *)
  counter(CU := part_seen AND running,
          R  := trig_ack.Q,
          PV := BATCH_SIZE);

  batch_done := counter.Q;

  (* ------------------------------------------------------------------
     5. SAÍDAS

     A trava FECHA quando a batelada completa: as peças que ainda vêm
     ficam represadas até alguém reconhecer. A esteira continua andando
     de propósito — é assim que se forma pulmão de acumulação, e as
     peças encostam na trava em vez de a linha inteira parar.
     ------------------------------------------------------------------ *)
  gate_close := batch_done OR NOT running;
  belt_run   := running;

  lamp_run   := running;
  lamp_batch := batch_done;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : contagem;
  END_RESOURCE
END_CONFIGURATION
