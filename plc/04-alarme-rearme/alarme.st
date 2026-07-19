(*
  Alarme com rearme e sinaleiro — cenário 04 da biblioteca de lógica de CLP.

  A planta detecta ATOLAMENTO: se o sensor fica bloqueado por mais de 3 s
  com a máquina rodando, é porque a peça parou de andar. A esteira para, o
  sinaleiro vermelho PISCA, e o alarme fica travado até alguém reconhecer.

  A botoeira BLOQUEIO fecha a trava e provoca o atolamento de propósito —
  é o jeito de exercitar a falha sem quebrar nada.

  O conceito central é que um alarme tem DUAS dimensões independentes:

      a condição física está presente?   (ativa / inativa)
      alguém já viu?                     (reconhecido / não reconhecido)

  Quatro combinações, e a que quase todo iniciante esquece é
  "condição sumiu mas ninguém viu" — o alarme NÃO pode se apagar sozinho,
  senão a falha some do painel sem nunca ter sido notada.

  Endereços:
    Forja DI 0   -> %IX0.0  botoeira START
    Forja DI 1   -> %IX0.1  botoeira STOP
    Forja DI 2   -> %IX0.2  botoeira RECONHECER (ack)
    Forja DI 3   -> %IX0.3  botoeira REARMAR (reset)
    Forja DI 4   -> %IX0.4  botoeira BLOQUEIO (segure para atolar)
    Forja DI 5   -> %IX0.5  sensor fotoelétrico
    Forja coil 0 -> %QX0.0  esteira (run)
    Forja coil 1 -> %QX0.1  trava (close)
    Forja coil 2 -> %QX0.2  sinaleiro VERDE — planta rodando
    Forja coil 3 -> %QX0.3  sinaleiro VERMELHO — pisca/fixo, ver abaixo
*)

PROGRAM alarme
  VAR
    btn_start  AT %IX0.0 : BOOL;
    btn_stop   AT %IX0.1 : BOOL;
    btn_ack    AT %IX0.2 : BOOL;
    btn_reset  AT %IX0.3 : BOOL;
    btn_block  AT %IX0.4 : BOOL;
    sensor     AT %IX0.5 : BOOL;

    belt_run   AT %QX0.0 : BOOL;
    gate_close AT %QX0.1 : BOOL;
    lamp_run   AT %QX0.2 : BOOL;
    lamp_alarm AT %QX0.3 : BOOL;

    running : BOOL := FALSE;

    (* As duas dimensões do alarme, guardadas separadas de propósito. *)
    alarm_active : BOOL := FALSE;   (* a falha ocorreu e não foi rearmada *)
    alarm_acked  : BOOL := FALSE;   (* alguém apertou reconhecer          *)

    jam_timer  : TON;
    trig_ack   : R_TRIG;
    trig_reset : R_TRIG;

    (* Oscilador para o pisca. Dois TON se realimentando: é o jeito
       portátil de gerar clock em ST, sem depender de bloco proprietário. *)
    t_off : TON;
    t_on  : TON;
    blink : BOOL;
  END_VAR

  (* ------------------------------------------------------------------
     1. OSCILADOR DO PISCA — 400 ms aceso, 400 ms apagado.

     t_off conta enquanto t_on ainda não estourou; quando t_off estoura,
     blink sobe e t_on começa a contar; quando t_on estoura, ele derruba
     a entrada de t_off, que zera e recomeça o ciclo.
     ------------------------------------------------------------------ *)
  t_off(IN := NOT t_on.Q, PT := T#400ms);
  t_on(IN := t_off.Q,     PT := T#400ms);
  blink := t_off.Q;

  (* ------------------------------------------------------------------
     2. DETECÇÃO DE ATOLAMENTO

     Sensor bloqueado por 3 s COM A MÁQUINA RODANDO. O "AND running" é
     essencial: parado, é normal a peça ficar na frente do sensor, e sem
     essa condição a máquina alarmaria toda vez que fosse desligada com
     peça no ponto de leitura.
     ------------------------------------------------------------------ *)
  jam_timer(IN := sensor AND running, PT := T#3s);

  (* ------------------------------------------------------------------
     3. O LATCH DO ALARME

     O alarme trava. A condição pode durar meio segundo, o registro dela
     dura até alguém tratar. É a diferença entre um alarme e uma lâmpada
     ligada num sensor.
     ------------------------------------------------------------------ *)
  IF jam_timer.Q THEN
    alarm_active := TRUE;
  END_IF;

  (* RECONHECER: "eu vi". Não conserta nada, não libera nada — só cala o
     pisca. Por borda, para reconhecer exigir uma ação e não um dedo
     apoiado no botão. *)
  trig_ack(CLK := btn_ack);
  IF trig_ack.Q AND alarm_active THEN
    alarm_acked := TRUE;
  END_IF;

  (* REARMAR: "pode voltar". Só funciona com as DUAS condições:
       - já reconhecido (alguém viu)
       - a falha física sumiu (NOT sensor — o feixe está livre)

     Rearmar com a peça ainda atravessada apagaria o alarme e a máquina
     tentaria andar contra o atolamento. Não se rearma defeito presente. *)
  trig_reset(CLK := btn_reset);
  IF trig_reset.Q AND alarm_acked AND NOT sensor THEN
    alarm_active := FALSE;
    alarm_acked  := FALSE;
  END_IF;

  (* ------------------------------------------------------------------
     4. SELO DE PARTIDA — derrubado pelo alarme.
     ------------------------------------------------------------------ *)
  IF alarm_active THEN
    running := FALSE;
  ELSE
    running := (btn_start OR running) AND NOT btn_stop;
  END_IF;

  (* ------------------------------------------------------------------
     5. SAÍDAS

     O sinaleiro carrega DUAS informações numa lâmpada só, e essa
     convenção é padrão de chão de fábrica:

        piscando -> falha nova, ninguém viu ainda
        fixo     -> falha reconhecida, ainda não resolvida
        apagado  -> normal

     Um operador do outro lado do galpão distingue os três de relance.
     ------------------------------------------------------------------ *)
  gate_close := btn_block;
  belt_run   := running;

  lamp_run   := running;
  lamp_alarm := (alarm_active AND NOT alarm_acked AND blink)
             OR (alarm_active AND alarm_acked);
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : alarme;
  END_RESOURCE
END_CONFIGURATION
