(*
  Partida/parada com selo — cenário 01 da biblioteca de lógica de CLP.

  A planta: uma esteira, uma botoeira de partida, uma de parada e um
  sinaleiro. Aperta START, a esteira roda e continua rodando depois que
  você solta o botão. Aperta STOP, ela para.

  O ponto do exercício é a segunda frase: "continua rodando depois que
  você solta". Isso é o SELO, e é a primeira coisa que se aprende em
  comando elétrico. A explicação completa está no README ao lado.

  Endereços (a Forja é o dispositivo remoto; o OpenPLC v4 mapeia I/O
  remoto a partir de %IX0.0 / %QX0.0):

    Forja DI 0   -> %IX0.0  botoeira START (pressed)
    Forja DI 1   -> %IX0.1  botoeira STOP  (pressed)
    Forja coil 0 -> %QX0.0  esteira (run)
    Forja coil 1 -> %QX0.1  sinaleiro "ligado" (on)

  ATENÇÃO À BOTOEIRA DE PARADA: aqui ela é lida como NA (normalmente
  aberta), porque é assim que a Forja modela `hmi.button` — o bit vai a 1
  ENQUANTO o botão está pressionado. Num painel de verdade a parada é
  NF (normalmente fechada), e a linha muda. O README explica por que essa
  diferença é de segurança e não de estilo, e mostra as duas versões.
*)

PROGRAM partida_parada
  VAR
    btn_start AT %IX0.0 : BOOL;   (* botoeira de partida, NA *)
    btn_stop  AT %IX0.1 : BOOL;   (* botoeira de parada — ver nota acima *)

    motor     AT %QX0.0 : BOOL;   (* esteira: a saída selada *)
    lamp_run  AT %QX0.1 : BOOL;   (* sinaleiro de planta ligada *)
  END_VAR

  (*
    O SELO.

    Leia como duas partidas somadas:
      btn_start  -> liga agora, enquanto o dedo está no botão
      motor      -> continua ligado porque JÁ ESTAVA ligado

    O `OR motor` é a saída se realimentando: é literalmente um contato
    auxiliar do próprio contator em paralelo com a botoeira. É por isso
    que se chama selo — ele "sela" o circuito fechado depois que a
    botoeira volta a abrir.

    O `AND NOT btn_stop` fica FORA do parêntese de propósito. Assim ele
    corta os dois caminhos ao mesmo tempo, e a parada vence mesmo se
    alguém estiver segurando o START. Isso se chama PARADA DOMINANTE, e
    é obrigatório: a alternativa deixaria a máquina partir com o botão de
    emergência apertado.
  *)
  motor := (btn_start OR motor) AND NOT btn_stop;

  lamp_run := motor;
END_PROGRAM


CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#50ms, PRIORITY := 0);
    PROGRAM instance0 WITH task0 : partida_parada;
  END_RESOURCE
END_CONFIGURATION
