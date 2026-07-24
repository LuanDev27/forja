(* Controle negativo: erro de SINTAXE. Este arquivo TEM de falhar.
   Um validador que aprova tudo não valida nada. *)
PROGRAM erro_de_sintaxe
  VAR
    a AT %IW0 : UINT;
    b AT %QW0 : UINT;
  END_VAR

  b := ;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : erro_de_sintaxe;
  END_RESOURCE
END_CONFIGURATION
