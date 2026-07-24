# Valida todos os .st da Forja contra o compilador STruC++ de verdade -- o
# mesmo que o OpenPLC Editor instalado usa -- sem abrir a GUI.
#
#   .\tools\openplc-validate\validar.ps1
#
# Passo 0: extrai o compilador de dentro do Editor (uma vez -- ver extrair.js).
# Passo 1: CONTROLES NEGATIVOS. Dois .st quebrados de proposito que TEM de
#          falhar. Se passarem, o harness esta cego e todo "OK" abaixo seria
#          mentira -- o script aborta.
# Passo 2: compila a biblioteca inteira.
#
# (Sem acentos de proposito: o PowerShell 5.1 le .ps1 sem BOM como ANSI.)

$ErrorActionPreference = 'Stop'

$raiz       = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ferramenta = $PSScriptRoot
$extraido   = Join-Path $ferramenta '.strucpp'

$editor = if ($env:OPENPLC_EDITOR_DIR) { $env:OPENPLC_EDITOR_DIR }
          else { "$env:LOCALAPPDATA\Programs\open-plc-editor" }
$exe = Join-Path $editor 'OpenPLC Editor.exe'

if (-not (Test-Path $exe)) {
  Write-Error "OpenPLC Editor nao encontrado em '$editor'. Aponte OPENPLC_EDITOR_DIR para a instalacao."
}

# O binario do Editor e Electron: com esta variavel ele roda como Node puro.
$env:ELECTRON_RUN_AS_NODE = '1'
$env:OPENPLC_EDITOR_DIR   = ($editor -replace '\\', '/')

function Invoke-Node {
  param([string[]]$Argumentos)
  $texto = & $exe $Argumentos | Out-String
  return [pscustomobject]@{ Codigo = $LASTEXITCODE; Texto = $texto }
}

# ---- 0. extrair o compilador (so na primeira vez) ----
if (-not (Test-Path (Join-Path $extraido 'node_modules\strucpp\dist\index.js'))) {
  Write-Host '== extraindo o STruC++ de dentro do Editor ==' -ForegroundColor Cyan
  $r = Invoke-Node @((Join-Path $ferramenta 'extrair.js'), $extraido)
  Write-Host $r.Texto
  if ($r.Codigo -ne 0) { Write-Error 'falha ao extrair o compilador' }
}

Copy-Item (Join-Path $ferramenta 'compilar.mjs') (Join-Path $extraido 'compilar.mjs') -Force
$compilar = Join-Path $extraido 'compilar.mjs'

# ---- 1. controles negativos: TEM de falhar ----
Write-Host '== controles negativos (precisam FALHAR) ==' -ForegroundColor Cyan
$negativos = @(Get-ChildItem (Join-Path $ferramenta 'controles-negativos') -Filter '*.st' |
               Select-Object -ExpandProperty FullName)
$r = Invoke-Node (@($compilar) + $negativos)
Write-Host $r.Texto
if ($r.Codigo -eq 0) {
  Write-Error 'ABORTADO: os controles negativos PASSARAM. O harness nao esta enxergando erro.'
}
Write-Host '   (falharam, como deviam)' -ForegroundColor DarkGray
Write-Host ''

# ---- 2. a biblioteca de verdade ----
Write-Host '== cenarios da biblioteca (precisam PASSAR) ==' -ForegroundColor Cyan
$sts  = @(Get-ChildItem (Join-Path $raiz 'plc')  -Filter '*.st' -Recurse | Sort-Object FullName | Select-Object -ExpandProperty FullName)
$sts += @(Get-ChildItem (Join-Path $raiz 'demo') -Filter '*.st' -Recurse | Select-Object -ExpandProperty FullName)

$r = Invoke-Node (@($compilar) + $sts)
Write-Host $r.Texto
exit $r.Codigo
