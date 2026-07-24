(* Controle negativo: sintaxe perfeita, erro SEMÂNTICO. Este arquivo TEM de
   falhar — é a classe de erro que importa (tipo, escopo, conversão), e prova
   que o harness roda a análise semântica, não só o parser. *)
PROGRAM erro_semantico
  VAR
    nivel AT %IW0 : UINT;
    vel   AT %QW0 : UINT;
  END_VAR

  vel := naoDeclarada;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : erro_semantico;
  END_RESOURCE
END_CONFIGURATION
