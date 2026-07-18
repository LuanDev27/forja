<#
.SYNOPSIS
    Empacota a Forja num ZIP portátil para Windows x64 (T058 / RNF-06).

.DESCRIPTION
    Gera build/Forja-v1-win-x64.zip contendo o executável exportado, o
    runtime .NET embutido (a máquina de destino NÃO precisa ter .NET
    instalado), o catálogo de dispositivos e a demo do OpenPLC.

    O catálogo viaja SOLTO ao lado do executável de propósito: o loader é
    da camada 1 (System.IO puro, sem Godot), então precisa de arquivos de
    verdade no disco — dentro do .pck ele não enxergaria nada. De quebra o
    usuário pode abrir e estender os tipos.

.PARAMETER Godot
    Caminho do executável do Godot (.NET/mono). Se omitido, tenta o
    caminho padrão da máquina de desenvolvimento e o PATH.

.PARAMETER Version
    Rótulo de versão usado no nome do ZIP. Padrão: v1.

.EXAMPLE
    pwsh build/package.ps1
    pwsh build/package.ps1 -Godot "C:\godot\Godot_mono.exe" -Version v1.1
#>
[CmdletBinding()]
param(
    [string]$Godot,
    [string]$Version = "v1"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo "build\out"
$stageDir = Join-Path $repo "build\stage"
$zipPath = Join-Path $repo "build\Forja-$Version-win-x64.zip"

function Resolve-Godot {
    if ($Godot) { return $Godot }
    $candidates = @(
        "$env:USERPROFILE\.local\godot\Godot_v4.4.1-stable_mono_win64\Godot_v4.4.1-stable_mono_win64_console.exe",
        "godot"
    )
    foreach ($c in $candidates) {
        $cmd = Get-Command $c -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    throw "Godot .NET não encontrado. Passe -Godot <caminho do executável>."
}

$godotExe = Resolve-Godot
Write-Host "Godot: $godotExe"

# .NET user-local: o export dispara `dotnet publish` por baixo.
$dotnetRoot = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
if (Test-Path $dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}

Write-Host "== 1/4 Build da solution (Release) =="
& dotnet build (Join-Path $repo "Forja.Studio.sln") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou." }

Write-Host "== 2/4 Export do Godot (Windows x64) =="
foreach ($dir in @($outDir, $stageDir)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

& $godotExe --headless --path $repo --export-release "Windows x64" (Join-Path $outDir "Forja.exe")
if ($LASTEXITCODE -ne 0) { throw "export do Godot falhou (templates de export instalados?)." }
if (-not (Test-Path (Join-Path $outDir "Forja.exe"))) { throw "export não gerou Forja.exe." }

Write-Host "== 3/4 Montando o pacote =="
Copy-Item (Join-Path $outDir "*") -Destination $stageDir -Recurse -Force

# Dados que precisam existir como arquivo no disco (ver .DESCRIPTION).
Copy-Item (Join-Path $repo "catalog") -Destination $stageDir -Recurse -Force
Copy-Item (Join-Path $repo "demo") -Destination $stageDir -Recurse -Force

@"
Forja $Version — simulador de plantas industriais (Windows x64)

Como usar
  1. Extraia esta pasta inteira em qualquer lugar (não precisa instalar nada,
     nem .NET: o runtime já vem junto).
  2. Rode Forja.exe.
  3. Abrir… -> demo\separador-altura.forja para a planta de demonstração.

Com PLC de verdade
  demo\openplc\README.md explica como ligar o OpenPLC v4 na demo por
  Modbus TCP (a Forja é o dispositivo remoto, porta 5020).

Conteúdo
  Forja.exe             executável
  data_Forja_*\         runtime .NET + assemblies (não apague)
  catalog\devices\*.json  catálogo de dispositivos (pode ser estendido)
  demo\                 cenas de exemplo e programa OpenPLC
"@ | Set-Content -Path (Join-Path $stageDir "LEIA-ME.txt") -Encoding UTF8

Write-Host "== 4/4 Compactando =="
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "OK: $zipPath ($sizeMb MB)" -ForegroundColor Green
Write-Host "Teste em maquina limpa (RNF-06): extrair e rodar Forja.exe num Windows sem .NET."
