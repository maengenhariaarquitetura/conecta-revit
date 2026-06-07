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

# ── Helper: gravar arquivo texto em UTF-8 SEM BOM ──────────────────────────
# PowerShell 5.1: -Encoding utf8 grava UTF-8 COM BOM (quebra JSON.parse no Node).
# [System.IO.File]::WriteAllText com UTF8Encoding($false) é a única opção segura em PS 5.1.
# PS 6+ tem -Encoding utf8NoBOM, mas não pode ser assumido aqui.
function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $enc = New-Object System.Text.UTF8Encoding($false)   # $false = sem BOM
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

Write-Host "==> ConectaRevit build v$Version" -ForegroundColor Cyan

# -------------------------------------------------------------------------
# 1. Carimbar versão
# -------------------------------------------------------------------------
Write-Host "`n--> [1/4] Carimbando versao $Version"

# Carimba package.json da ponte (version)
$pkgPath = Join-Path $BridgeDir 'package.json'
$pkg = Get-Content $pkgPath -Raw | ConvertFrom-Json
$pkg.version = $Version
Write-Utf8NoBom $pkgPath ($pkg | ConvertTo-Json -Depth 10)

# Carimba manifest.json da ponte (version)
$mfPath = Join-Path $BridgeDir 'manifest.json'
$mf = Get-Content $mfPath -Raw | ConvertFrom-Json
$mf.version = $Version
Write-Utf8NoBom $mfPath ($mf | ConvertTo-Json -Depth 10)

# Carimba AppVersion no setup.iss (substitui a linha #define AppVersion "...")
$issContent = (Get-Content $SetupIss -Raw) -replace '#define AppVersion\s+"[^"]+"', "#define AppVersion `"$Version`""
Write-Utf8NoBom $SetupIss $issContent

# TODO: carimbar <Version> nas propriedades dos .csproj ou em AssemblyInfo.

# -------------------------------------------------------------------------
# 2. Compilar add-in
# -------------------------------------------------------------------------
Write-Host "`n--> [2/4] Compilando add-in (Release)"
$buildArgs = @('build', $AddinSln, '-c', 'Release')
if ($RevitApiDir) { $buildArgs += "/p:RevitApiDir=$RevitApiDir" }
dotnet @buildArgs

# -------------------------------------------------------------------------
# 3. Build da ponte + mcpb pack
# -------------------------------------------------------------------------
Write-Host "`n--> [3/4] Build da ponte (npm run build) + mcpb pack"
Push-Location $BridgeDir
try   { node (Join-Path $RepoRoot 'installer\build-mcpb.js') }
finally { Pop-Location }

# -------------------------------------------------------------------------
# 4. Installer Inno Setup
# -------------------------------------------------------------------------
Write-Host "`n--> [4/4] Installer (ISCC.exe)"
if (-not (Test-Path $Iscc)) {
    Write-Warning "ISCC.exe nao encontrado em '$Iscc'. Instale o Inno Setup 6 para gerar o .exe."
} else {
    & $Iscc $SetupIss
}

Write-Host "`n==> Build concluido: ConectaRevit v$Version" -ForegroundColor Green
