(*
  Intertravamento e emergência — cenário 02 da biblioteca de lógica de CLP.

  A planta: a esteira do cenário 01, agora com um pistão desviador, um
  sensor fotoelétrico, botoeira de EMERGÊNCIA e botoeira de REARME.

  Três coisas novas em relação ao cenário 01:

    1. INTERTRAVAMENTO — esteira e pistão não podem agir ao mesmo tempo.
       A esteira só anda com o pistão recolhido; o pistão só avança com a
       esteira parada.
    2. EMERGÊNCIA TRAVADA — apertou, tudo desliga e FICA desligado.
       Soltar a botoeira não religa nada.
    3. REARME OBRIGATÓRIO — só o botão de rearme libera a máquina, e
       mesmo assim ela ainda espera o START.

  Endereços:
    Forja DI 0   -> %IX0.0  botoeira START
    Forja DI 1   -> %IX0.1  botoeira STOP
    Forja DI 2   -> %IX0.2  botoeira EMERGÊNCIA
    Forja DI 3   -> %IX0.3  botoeira REARME
    Forja DI 4   -> %IX0.4  sensor fotoelétrico (peça na posição)
    Forja DI 5   -> %IX0.5  fim de curso do pistão (extended)
    Forja coil 0 -> %QX0.0  esteira (run)
    Forja coil 1 -> %QX0.1  pistão desviador (extend)
    Forja coil 2 -> %QX0.2  sinaleiro VERDE  — planta ligada
    Forja coil 3 -> %QX0.3  sinaleiro VERMELHO — emergência ativa

  LEIA O README ANTES DE USAR ISTO COMO REFERÊNCIA. Uma emergência de
  verdade NÃO é feita em software: é circuito cabeado, com relé de
  segurança, cortando a energia dos atuadores. O que está aqui é a parte
  que o CLP faz — saber que a emergência ocorreu e não religar sozinho.
*)

PROGRAM intertravamento
  VAR
    btn_start       AT %IX0.0 : BOOL;
    btn_stop        AT %IX0.1 : BOOL;
    btn_emerg       AT %IX0.2 : BOOL;   (* NA na Forja; NF no painel real *)
    btn_reset       AT %IX0.3 : BOOL;
    sensor_part     AT %IX0.4 : BOOL;
    piston_extended AT %IX0.5 : BOOL;   (* realimentação FÍSICA do pistão *)

    belt_run        AT %QX0.0 : BOOL;
    piston_extend   AT %QX0.1 : BOOL;
    lamp_run        AT %QX0.2 : BOOL;
    lamp_emerg      AT %QX0.3 : BOOL;
  END_VAR

  VAR
    (* Nasce TRUE de propósito: na energização a máquina está travada e
       exige rearme deliberado. Ver "Partida a frio" no README. *)
    emergency : BOOL := TRUE;

    running   : BOOL := FALSE;   (* selo de partida, igual ao cenário 01 *)
    diverting : BOOL := FALSE;   (* ciclo de desvio em andamento *)

    trig_part  : R_TRIG;
    trig_reset : R_TRIG;
    retract_tmr : TON;
  END_VAR

  (* ------------------------------------------------------------------
     1. EMERGÊNCIA — trava por nível, destrava só por borda de rearme.
     ------------------------------------------------------------------ *)

  (* Nível, não borda: enquanto a botoeira estiver apertada, a trava é
     reafirmada a cada ciclo. Ninguém "escapa" da emergência segurando
     outro botão. *)
  IF btn_emerg THEN
    emergency := TRUE;
  END_IF;

  (* O rearme é por BORDA e exige a emergência já liberada. Se fosse por
     nível, dava para segurar o rearme, soltar a emergência e a máquina
     se liberaria sozinha no mesmo instante — burlando o intertravamento
     com um dedo em cada botão. *)
  trig_reset(CLK := btn_reset);
  IF trig_reset.Q AND NOT btn_emerg THEN
    emergency := FALSE;
  END_IF;

  (* ------------------------------------------------------------------
     2. SELO DE PARTIDA — o mesmo do cenário 01, agora subordinado.
     ------------------------------------------------------------------ *)

  IF emergency THEN
    running := FALSE;          (* emergência derruba o selo *)
  ELSE
    running := (btn_start OR running) AND NOT btn_stop;
  END_IF;

  (* ------------------------------------------------------------------
     3. CICLO DE DESVIO — peça detectada dispara o pistão.
     ------------------------------------------------------------------ *)

  trig_part(CLK := sensor_part);
  IF running AND trig_part.Q THEN
    diverting := TRUE;
  END_IF;

  (* Pistão no fim de curso há 500 ms => peça empurrada, pode recolher. *)
  retract_tmr(IN := diverting AND piston_extended, PT := T#500ms);
  IF retract_tmr.Q OR NOT running THEN
    diverting := FALSE;
  END_IF;

  (* ------------------------------------------------------------------
     4. INTERTRAVAMENTO — o coração deste cenário.

     Repare no que cada condição olha:

       belt_run usa NOT piston_extended  -> o SENSOR de fim de curso
       piston_extend usa NOT belt_run    -> a saída já calculada acima

     A esteira se intertrava contra a REALIDADE FÍSICA (o fim de curso),
     não contra o comando. Comando diz "mandei recolher"; sensor diz
     "está recolhido". Só o segundo serve para intertravar — a mangueira
     pode ter furado, o ar pode ter caído, a haste pode estar emperrada.

     A ordem das duas linhas importa: em ST as atribuições rodam de cima
     para baixo, então piston_extend enxerga o belt_run DESTE ciclo, já
     atualizado. Em ladder é a mesma coisa com a ordem dos degraus.
     ------------------------------------------------------------------ *)

  belt_run := running AND NOT diverting AND NOT piston_extended;

  piston_extend := diverting AND NOT belt_run AND NOT emergency;

  (* ------------------------------------------------------------------
     5. SINALIZAÇÃO
     ------------------------------------------------------------------ *)

  lamp_run   := running;
  lamp_emerg := emergency;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : intertravamento;
  END_RESOURCE
END_CONFIGURATION
