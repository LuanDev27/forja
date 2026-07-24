(*
  Controle de nível — cenário 07 da biblioteca de lógica de CLP.

  O primeiro cenário que age sobre um NÚMERO. Os seis anteriores liam
  bits e escreviam bits: ligado/desligado, presente/ausente. Aqui o CLP
  lê uma medida contínua (o nível do pulmão, 0–100 %) e escreve uma
  ordem contínua (a velocidade da esteira de saída, 0–2 m/s).

  A planta: peças caem num pulmão. Um sensor de nível mede quanto
  material está acumulado. Uma esteira de velocidade variável drena o
  pulmão. Encheu demais? Drena rápido. Esvaziou? Drena devagar, para
  não secar a linha de baixo.

  Conceitos novos: palavra em vez de bit, reescala de bruto para
  unidade de engenharia, setpoint e — o que separa quem já apanhou de
  quem ainda vai apanhar — BANDA MORTA.

  Endereços:
    Forja input register 0   -> %IW0  nível do pulmão (bruto 0..65535)
    Forja holding register 0 -> %QW0  velocidade da esteira (bruto 0..65535)
*)

PROGRAM controle_nivel
  VAR CONSTANT
    (* O setpoint, em unidade de engenharia (%), e não em contagem
       bruta. Esse é o número que o operador discute — "quero o pulmão
       em 60 %" — e é nessa unidade que ele tem de aparecer no código. *)
    SP_NIVEL : UINT := 60;

    (* A banda morta, também em %. Ver a seção 3. *)
    BANDA : UINT := 5;

    (* As duas velocidades, em contagem bruta de 0..65535 sobre a faixa
       0..2 m/s declarada na cena:
         16384 / 65535 * 2 m/s ~ 0,50 m/s
         49151 / 65535 * 2 m/s ~ 1,50 m/s *)
    VEL_LENTA  : UINT := 16384;
    VEL_RAPIDA : UINT := 49151;
  END_VAR

  VAR
    nivel_raw AT %IW0 : UINT;
    vel_raw   AT %QW0 : UINT;
  END_VAR

  VAR
    nivel_pct : UINT;             (* nível já em %, 0..100 *)
    drenando  : BOOL := FALSE;    (* memória da banda morta *)
  END_VAR

  (* ------------------------------------------------------------------
     1. REESCALA — bruto para unidade de engenharia

     O fio carrega CONTAGEM, não centímetro. O cartão analógico entrega
     0..65535 e cabe ao programa dizer o que essa contagem significa.
     A Forja faz a conversão do outro lado (o simulador sabe que o
     tanque tem 0..100 %), mas o CLP não recebe essa informação: ele
     recebe um número de 16 bits e mais nada.

     Por isso a reescala aparece explícita aqui, na primeira linha da
     lógica. Todo raciocínio abaixo dela é em %, nunca em contagem.
     ------------------------------------------------------------------ *)
  nivel_pct := DINT_TO_UINT(UINT_TO_DINT(nivel_raw) * 100 / 65535);

  (* ------------------------------------------------------------------
     2. POR QUE UINT E NÃO INT

     %IW é uma palavra de 16 bits SEM sinal: 0..65535. Declarada como
     INT (com sinal, −32768..32767), toda leitura acima de metade da
     escala vira número negativo — o tanque passa de 50 % e o programa
     acha que ele está vazio. É o erro clássico de quem chega ao
     analógico vindo do digital.

     E a conta da linha acima passa por DINT de propósito: 65535 * 100
     dá 6.553.500, que não cabe em 16 bits. Multiplicar antes de
     dividir mantém a precisão; alargar o tipo antes de multiplicar
     evita o estouro. As duas coisas ao mesmo tempo, ou uma delas
     estraga a outra.
     ------------------------------------------------------------------ *)

  (* ------------------------------------------------------------------
     3. BANDA MORTA — o coração deste cenário

     A tentação é escrever:

         IF nivel_pct > SP_NIVEL THEN vel_raw := VEL_RAPIDA;
         ELSE                         vel_raw := VEL_LENTA;  END_IF;

     e isso funciona por uns segundos. Depois a esteira começa a
     chacoalhar entre 0,5 e 1,5 m/s várias vezes por segundo.

     O motivo: um sinal analógico REAL nunca fica parado. Ruído
     elétrico, ondulação da superfície do material e a própria
     quantização do cartão (uma contagem vale 100/65535 de %) fazem a
     leitura tremer em torno do setpoint. Comparação sem banda morta
     transforma esse tremor em chaveamento — e chaveamento a 20 Hz
     destrói contator, inversor e paciência.

     A banda morta separa o ponto de LIGAR do ponto de DESLIGAR:

         liga a drenagem rápida em 65 %   (SP + BANDA)
         volta para devagar em 55 %       (SP - BANDA)

     Entre 55 e 65 nada muda: a saída fica onde estava. É por isso que
     `drenando` é uma variável com memória e não uma expressão — a
     resposta depende de onde o processo VEIO, não só de onde ele está.
     ------------------------------------------------------------------ *)
  IF nivel_pct >= SP_NIVEL + BANDA THEN
    drenando := TRUE;
  ELSIF nivel_pct <= SP_NIVEL - BANDA THEN
    drenando := FALSE;
  END_IF;

  (* ------------------------------------------------------------------
     4. SAÍDA ANALÓGICA

     Duas velocidades, não um PID. O objetivo deste cenário é provar o
     ciclo completo — ler número, decidir sobre número, escrever número
     — e não sintonizar um controlador. O PID entra quando a planta
     pedir, e aí já existe caminho para ele.

     A escrita também é em contagem bruta: a Forja reescala 0..65535
     de volta para 0..2 m/s na fronteira dela, com a mesma faixa que a
     cena declara. Trocar a faixa da cena sem trocar as constantes
     daqui é como trocar o cartão sem avisar o programa.
     ------------------------------------------------------------------ *)
  IF drenando THEN
    vel_raw := VEL_RAPIDA;
  ELSE
    vel_raw := VEL_LENTA;
  END_IF;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : controle_nivel;
  END_RESOURCE
END_CONFIGURATION
