(*
  Separador por altura — programa de demonstração da Forja (T053, RF-09).

  Lógica:
    - O botão Start (na Forja) alterna liga/desliga da planta.
    - A esteira roda enquanto a planta está ligada e o pistão recolhido.
    - Peça ALTA na frente do sensor => para a esteira, estende o pistão
      (desvia a peça para a calha lateral), espera o fim de curso, recolhe
      e religa a esteira. Peças baixas seguem direto até o fim da esteira.

  Endereços (contracts/modbus-mapping.md — a Forja é o dispositivo remoto;
  o OpenPLC v4 mapeia I/O remoto a partir de %IX0.0 / %QX0.0, conforme a
  coluna Address do IO Tag Mapping; o v3 antigo usava %IX100.0):

    Forja DI 0  -> %IX0.0  sensor de altura (detect)
    Forja DI 1  -> %IX0.1  fim de curso do pistão (extended)
    Forja DI 2  -> %IX0.2  botão Start (pressed)
    Forja coil 0 -> %QX0.0 esteira principal (run)
    Forja coil 1 -> %QX0.1 pistão desviador (extend)
    Forja coil 2 -> %QX0.2 luz indicadora (on)
*)

PROGRAM separador
  VAR
    height_detect   AT %IX0.0 : BOOL;
    piston_extended AT %IX0.1 : BOOL;
    start_button    AT %IX0.2 : BOOL;
    belt_run        AT %QX0.0 : BOOL;
    piston_extend   AT %QX0.1 : BOOL;
    run_light       AT %QX0.2 : BOOL;
  END_VAR

  VAR
    running : BOOL := FALSE;   (* planta ligada (Start alterna) *)
    pushing : BOOL := FALSE;   (* ciclo de desvio em andamento *)

    trig_start : R_TRIG;
    trig_tall  : R_TRIG;
    retract_tmr : TON;
  END_VAR

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

  (* Pistão no fim de curso ha 600 ms => peça desviada, recolhe. *)
  retract_tmr(IN := pushing AND piston_extended, PT := T#600ms);
  IF retract_tmr.Q THEN
    pushing := FALSE;
  END_IF;

  (* Desligar a planta no meio de um ciclo tambem recolhe o pistão. *)
  IF NOT running THEN
    pushing := FALSE;
  END_IF;

  piston_extend := pushing;
  belt_run := running AND NOT pushing;
  run_light := running;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : separador;
  END_RESOURCE
END_CONFIGURATION
