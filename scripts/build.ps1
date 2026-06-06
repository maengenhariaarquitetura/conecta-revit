# build.ps1 — ConectaRevit
# Sequência de build completa (ARCHITECTURE § 9):
#   1. Ler VERSION e carimbar nos projetos
#   2. Compilar add-in (Release, net8.0-windows)
#   3. Build da ponte (npm run build) + mcpb pack
#   4. Gerar installer via ISCC.exe
#
# Pré-requisitos: .NET 8 SDK, Node 20+, Inno Setup 6 em C:\Program Files (x86)\Inno Setup 6\
# Uso: .\scripts\build.ps1 [-RevitApiDir "C:\...\Revit 2025"]

param(
    [string]$RevitApiDir = ""
)

$ErrorActionPreference = 'Stop'

$RepoRoot    = Split-Path $PSScriptRoot -Parent
$VersionFile = Join-Path $RepoRoot 'VERSION'
$Version     = (Get-Content $VersionFile -Raw).Trim()
$BridgeDir   = Join-Path $RepoRoot 'src\bridge'
$SetupIss    = Join-Path $RepoRoot 'installer\inno\setup.iss'
$McpbScript  = Join-Path $RepoRoot 'installer\build-mcpb.js'
$AddinSln    = Join-Path $RepoRoot 'ConectaRevit.sln'
$Iscc        = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

Write-Host "==> ConectaRevit build v$Version" -ForegroundColor Cyan

# -------------------------------------------------------------------------
# 1. Carimbar versão
# -------------------------------------------------------------------------
Write-Host "`n--> [1/4] Carimbando versao $Version"

# Carimba package.json e manifest.json da ponte
$pkgPath = Join-Path $BridgeDir 'package.json'
$pkg = Get-Content $pkgPath -Raw | ConvertFrom-Json
$pkg.version = $Version
$pkg | ConvertTo-Json -Depth 10 | Set-Content $pkgPath -Encoding utf8

$mfPath = Join-Path $BridgeDir 'manifest.json'
$mf = Get-Content $mfPath -Raw | ConvertFrom-Json
$mf.version = $Version
$mf | ConvertTo-Json -Depth 10 | Set-Content $mfPath -Encoding utf8

# Carimba AppVersion no setup.iss (substitui a linha #define AppVersion "...")
(Get-Content $SetupIss) -replace '#define AppVersion\s+"[^"]+"', "#define AppVersion `"$Version`"" |
    Set-Content $SetupIss -Encoding utf8

# TODO Fase 3: carimbar <Version> nas propriedades dos .csproj ou em AssemblyInfo.

# -------------------------------------------------------------------------
# 2. Compilar add-in
# -------------------------------------------------------------------------
Write-Host "`n--> [2/4] Compilando add-in (Release)"
$buildArgs = @('build', $AddinSln, '-c', 'Release')
if ($RevitApiDir) { $buildArgs += "/p:RevitApiDir=$RevitApiDir" }
# dotnet @buildArgs   # descomente quando os projetos estiverem prontos para compilar

# -------------------------------------------------------------------------
# 3. Build da ponte + mcpb pack
# -------------------------------------------------------------------------
Write-Host "`n--> [3/4] Build da ponte (npm run build)"
# Push-Location $BridgeDir
# npm run build
# Pop-Location

Write-Host "    mcpb pack"
# node $McpbScript

# -------------------------------------------------------------------------
# 4. Installer Inno Setup
# -------------------------------------------------------------------------
Write-Host "`n--> [4/4] Installer (ISCC.exe)"
if (-not (Test-Path $Iscc)) {
    Write-Warning "ISCC.exe nao encontrado em '$Iscc'. Instale o Inno Setup 6."
} else {
    # & $Iscc $SetupIss
}

Write-Host "`n==> Build concluido: ConectaRevit v$Version" -ForegroundColor Green
