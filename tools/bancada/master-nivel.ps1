# Master Modbus TCP descartavel: fala com a Forja na 5020 e roda a MESMA
# logica do plc/07-controle-de-nivel/controle-nivel.st sobre o fio de verdade.
# Frames crus (sem biblioteca) para nao mascarar nada com abstracao.
#
# O parser confere o transaction id de cada resposta. Sem isso, um frame lido
# com um byte de deslocamento devolve silenciosamente o byte errado -- foi
# exatamente o que aconteceu na primeira versao (0xBFFF virava 255).

param(
  [string]$Alvo = '127.0.0.1',
  [int]$Porta = 5020,
  [int]$Ciclos = 40,
  [int]$IntervaloMs = 200,
  [int]$ForcarVel = -1,   # >=0: ignora a lei de controle e escreve esse bruto
  [switch]$Hex            # loga cada frame
)

$SP_NIVEL = 60; $BANDA = 5; $VEL_LENTA = 16384; $VEL_RAPIDA = 49151

$cli = New-Object System.Net.Sockets.TcpClient
$cli.Connect($Alvo, $Porta)
$s = $cli.GetStream()
$s.ReadTimeout = 3000
$script:tx = 0

function Read-Exato {
  param([int]$Quantos)
  $buf = New-Object byte[] $Quantos
  $lido = 0
  while ($lido -lt $Quantos) {
    $n = $s.Read($buf, $lido, $Quantos - $lido)
    if ($n -le 0) { throw 'conexao fechada pela Forja' }
    $lido += $n
  }
  return ,$buf
}

function Invoke-Modbus {
  param([byte]$Funcao, [int]$Endereco, [int]$Valor)

  $script:tx++
  $esperado = $script:tx
  $pdu = [byte[]]@($Funcao,
                   [byte](($Endereco -shr 8) -band 0xFF), [byte]($Endereco -band 0xFF),
                   [byte](($Valor    -shr 8) -band 0xFF), [byte]($Valor    -band 0xFF))
  $len = $pdu.Length + 1
  $mbap = [byte[]]@([byte](($esperado -shr 8) -band 0xFF), [byte]($esperado -band 0xFF),
                    0, 0,
                    [byte](($len -shr 8) -band 0xFF), [byte]($len -band 0xFF),
                    1)
  $quadro = $mbap + $pdu
  $s.Write($quadro, 0, $quadro.Length); $s.Flush()

  # MBAP: 6 bytes (tx, protocolo, tamanho). O unitId ja conta no tamanho.
  $cab = Read-Exato 6
  # [int] obrigatorio nos dois: ver a nota em Read-Registrador. Sem ele o
  # transaction id acima de 255 vira lixo e o proprio guarda contra
  # dessincronizacao passa a acusar erro que nao existe.
  $txResp  = (([int]$cab[0] -shl 8) -bor [int]$cab[1])
  $tamanho = (([int]$cab[4] -shl 8) -bor [int]$cab[5])
  $resto = Read-Exato $tamanho          # unitId + PDU
  if ($Hex) {
    Write-Host ("    -> " + (($quadro | % { $_.ToString('X2') }) -join ' '))
    Write-Host ("    <- " + (($cab | % { $_.ToString('X2') }) -join ' ') + ' | ' + (($resto | % { $_.ToString('X2') }) -join ' '))
  }
  if ($txResp -ne $esperado) {
    throw ("DESSINCRONIZADO: pedi tx={0}, veio tx={1}" -f $esperado, $txResp)
  }

  $pduResp = $resto[1..($resto.Length - 1)]   # tira o unitId
  if ($pduResp[0] -band 0x80) {
    throw ("excecao Modbus {0} na funcao {1}" -f $pduResp[1], $Funcao)
  }
  return ,$pduResp
}

# PDU de resposta FC03/FC04: [funcao][byteCount][hi][lo]
#
# ARMADILHA do PowerShell 5.1: `-shl` sobre um [byte] mascara em 8 bits, entao
# `[byte]0xBF -shl 8` da 0 (e nao 0xBF00) e so o byte baixo sobrevive. Foi o que
# fez 49151 virar 255 e 16384 virar 0 -- parecia defeito da Forja, era daqui.
# O [int] na frente e obrigatorio.
function Read-Registrador {
  param([byte]$Funcao, [int]$Off)
  $p = Invoke-Modbus -Funcao $Funcao -Endereco $Off -Valor 1
  return ((([int]$p[2] -shl 8) -bor [int]$p[3]))
}
function Write-HoldingRegister {
  param([int]$Off, [int]$Val)
  [void](Invoke-Modbus -Funcao 6 -Endereco $Off -Valor $Val)
}

Write-Host ("conectado em {0}:{1}" -f $Alvo, $Porta)
Write-Host 'ciclo | %IW0 (nivel) | pct | drenando | %QW0 escrito | %QW0 lido'

$drenando = $false
$niveis = @{}; $vels = @{}; $voltas = @{}

for ($i = 1; $i -le $Ciclos; $i++) {
  $nivel = Read-Registrador -Funcao 4 -Off 0     # FC04 input register
  $pct = [int][math]::Floor($nivel * 100 / 65535)

  if ($ForcarVel -ge 0) {
    $vel = $ForcarVel
  } else {
    if ($pct -ge ($SP_NIVEL + $BANDA)) { $drenando = $true }
    elseif ($pct -le ($SP_NIVEL - $BANDA)) { $drenando = $false }
    $vel = if ($drenando) { $VEL_RAPIDA } else { $VEL_LENTA }
  }

  Write-HoldingRegister 0 $vel                    # FC06
  $volta = Read-Registrador -Funcao 3 -Off 0      # FC03 holding register

  $niveis[$nivel] = $true; $vels[$vel] = $true; $voltas[$volta] = $true

  if ($i -le 5 -or $i % 5 -eq 0) {
    Write-Host ("{0,5} | {1,12} | {2,3} | {3,8} | {4,12} | {5}" -f $i, $nivel, $pct, $drenando, $vel, $volta)
  }
  Start-Sleep -Milliseconds $IntervaloMs
}

$s.Close(); $cli.Close()
Write-Host ''
Write-Host ("%IW0 distintos: {0} -> {1}" -f $niveis.Count, (($niveis.Keys | Sort-Object) -join ', '))
Write-Host ("%QW0 escritos : {0} -> {1}" -f $vels.Count,   (($vels.Keys   | Sort-Object) -join ', '))
Write-Host ("%QW0 lidos    : {0} -> {1}" -f $voltas.Count, (($voltas.Keys | Sort-Object) -join ', '))
