(*
  Pulmão com liberação por espaço — cenário 05 da biblioteca de CLP.

  A planta: uma esteira com uma TRAVA no meio. Antes dela as peças se
  acumulam (o pulmão). Depois dela existe uma ZONA que só comporta uma
  peça por vez. A trava só libera quando a zona está livre — e libera
  UMA peça, não a fila inteira.

  Três coisas novas:
    1. CTUD — contador bidirecional, para saber quantas peças estão
       represadas neste instante.
    2. TP — temporizador de PULSO, para abrir a trava por tempo medido.
    3. Singularização: soltar uma peça de uma fila é um problema de
       verdade, e não se resolve só abrindo a trava.

  Endereços:
    Forja DI 0   -> %IX0.0  botoeira START
    Forja DI 1   -> %IX0.1  botoeira STOP
    Forja DI 2   -> %IX0.2  sensor de ENTRADA do pulmão
    Forja DI 3   -> %IX0.3  sensor de PEÇA ESPERANDO na trava
    Forja DI 4   -> %IX0.4  sensor da ZONA de jusante
    Forja coil 0 -> %QX0.0  esteira (run)
    Forja coil 1 -> %QX0.1  trava (close)
    Forja coil 2 -> %QX0.2  sinaleiro VERDE — rodando
    Forja coil 3 -> %QX0.3  sinaleiro AMARELO — pulmão cheio
*)

PROGRAM pulmao
  VAR CONSTANT
    (* Capacidade do pulmão, em peças. Acima disso a linha de montante
       deveria ser avisada para parar de alimentar. *)
    BUFFER_MAX : INT := 5;
  END_VAR

  VAR
    btn_start    AT %IX0.0 : BOOL;
    btn_stop     AT %IX0.1 : BOOL;
    s_entry      AT %IX0.2 : BOOL;
    s_waiting    AT %IX0.3 : BOOL;
    s_zone       AT %IX0.4 : BOOL;

    belt_run     AT %QX0.0 : BOOL;
    gate_close   AT %QX0.1 : BOOL;
    lamp_run     AT %QX0.2 : BOOL;
    lamp_full    AT %QX0.3 : BOOL;
  END_VAR

  VAR
    running : BOOL := FALSE;

    occupancy : CTUD;      (* quantas peças estão no pulmão agora *)
    zone_free_tmr : TON;   (* a zona precisa estar livre e ESTÁVEL *)
    release : TP;          (* pulso de abertura da trava *)

    zone_free    : BOOL;
    release_req  : BOOL;
    buffer_full  : BOOL;
  END_VAR

  (* ------------------------------------------------------------------
     1. SELO DE PARTIDA — cenário 01.
     ------------------------------------------------------------------ *)
  running := (btn_start OR running) AND NOT btn_stop;

  (* ------------------------------------------------------------------
     2. OCUPAÇÃO DO PULMÃO — contador BIDIRECIONAL

     CTUD conta nos dois sentidos:
       CU  +1 por borda de subida  -> peça ENTROU no pulmão
       CD  -1 por borda de subida  -> peça SAIU (cruzou a zona)
       CV  ocupação atual
       QU  CV >= PV  (cheio)
       QD  CV <= 0   (vazio)

     Repare que a ocupação é DEDUZIDA de duas contagens, não medida
     diretamente. Isso é o normal em chão de fábrica — não existe sensor
     de "quantas peças tem na fila". E é por isso que a conta erra com o
     tempo: peça retirada na mão, sensor falho, energização no meio da
     produção. Todo pulmão real precisa de um jeito de zerar a conta
     quando a linha esvazia de verdade.
     ------------------------------------------------------------------ *)
  occupancy(CU := s_entry AND running,
            CD := s_zone AND running,
            R  := NOT running,
            PV := BUFFER_MAX);

  buffer_full := occupancy.QU;

  (* ------------------------------------------------------------------
     3. A ZONA ESTÁ LIVRE?

     Não basta o sensor estar apagado NESTE ciclo. A peça que acabou de
     sair ainda pode estar com a traseira no limite do feixe, ou o feixe
     pode ter piscado entre duas peças coladas. Exigir 500 ms de feixe
     livre e estável é o que impede liberar em cima da peça anterior.
     ------------------------------------------------------------------ *)
  zone_free_tmr(IN := NOT s_zone, PT := T#500ms);
  zone_free := zone_free_tmr.Q;

  (* ------------------------------------------------------------------
     4. O SEMÁFORO: liberar UMA peça

     A condição de liberação é a conjunção óbvia — tem peça esperando E
     a zona está livre E a máquina está rodando.

     O que NÃO é óbvio é o pulso. Abrir a trava enquanto a condição for
     verdadeira soltaria a fila inteira: assim que a primeira peça sai, a
     segunda encosta e passa junto, porque a trava ainda está aberta.

     TP resolve: na BORDA de subida da condição, ele mantém a saída
     ligada por um tempo fixo e ignora o que acontece com a entrada
     durante esse tempo. 700 ms a 0,5 m/s dá 35 cm — o suficiente para
     uma peça de 20 cm passar e a seguinte ainda não alcançar a trava.

     Esse tempo é dependente da velocidade da esteira e do tamanho da
     peça. Mudou a mecânica, revisa o número.
     ------------------------------------------------------------------ *)
  release_req := running AND s_waiting AND zone_free;

  release(IN := release_req, PT := T#700ms);

  (* ------------------------------------------------------------------
     5. SAÍDAS

     A trava FECHA por padrão e só abre durante o pulso. Fechada é o
     estado seguro: se o programa parar, a fila fica represada em vez de
     escorrer toda para a zona.
     ------------------------------------------------------------------ *)
  gate_close := NOT release.Q;
  belt_run   := running;

  lamp_run  := running;
  lamp_full := buffer_full;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : pulmao;
  END_RESOURCE
END_CONFIGURATION
