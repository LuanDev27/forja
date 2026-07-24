# Sobe a Forja headless com uma cena, ja em Run, como servidor Modbus TCP.
#
#   .\tools\bancada\subir-forja.ps1
#   .\tools\bancada\subir-forja.ps1 -Cena res://plc/01-partida-parada-selo/partida-parada.forja
#
# Depois, noutro terminal:  .\tools\bancada\master-nivel.ps1
#
# Headless de proposito: para exercitar o caminho de I/O nao e preciso janela,
# e assim o teste roda sem depender de GUI. Para VER a planta, abrir o app
# normal e usar Abrir... na cena.

param(
  [string]$Cena = 'res://plc/07-controle-de-nivel/controle-nivel.forja',
  [string]$Log  = "$env:TEMP\forja-bancada.log"
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$godot = if ($env:GODOT_EXE) { $env:GODOT_EXE }
         else { "$env:USERPROFILE\.local\godot\Godot_v4.4.1-stable_mono_win64\Godot_v4.4.1-stable_mono_win64_console.exe" }
if (-not (Test-Path $godot)) { Write-Error "Godot nao encontrado em '$godot'. Aponte GODOT_EXE." }

# O Godot mono so acha o runtime .NET com DOTNET_ROOT apontando para o SDK local.
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"

$erroLog = [IO.Path]::ChangeExtension($Log, '.err')
Remove-Item $Log, $erroLog -ErrorAction SilentlyContinue

# A aspa em volta da raiz e obrigatoria: o caminho do projeto tem espacos e o
# Godot corta no primeiro deles ("Invalid project path specified").
$linha = '--headless --path "{0}" -- --scene {1}' -f $raiz, $Cena
$p = Start-Process -FilePath $godot -ArgumentList $linha `
       -RedirectStandardOutput $Log -RedirectStandardError $erroLog `
       -WindowStyle Hidden -PassThru

Start-Sleep -Seconds 5
if ($p.HasExited) {
  Write-Host (Get-Content $Log -ErrorAction SilentlyContinue | Out-String)
  Write-Error 'a Forja saiu durante a subida'
}

Write-Host (Get-Content $Log -ErrorAction SilentlyContinue | Out-String)
$porta = Get-NetTCPConnection -LocalPort 5020 -State Listen -ErrorAction SilentlyContinue
if (-not $porta) { Write-Warning 'nada escutando na 5020 -- a cena declara driver modbus-tcp-server?' }
else { Write-Host 'escutando na 5020' -ForegroundColor Green }

Write-Host ''
Write-Host ("pid {0}. Para derrubar:  Stop-Process -Id {0}" -f $p.Id)
Write-Host ("log: {0}  |  erros: {1}" -f $Log, $erroLog)
