# release.ps1 — ConectaRevit
# Valida o estado do repositório, chama build.ps1 e prepara artefatos para GitHub Release.
# TODO Fase 7: automatizar publicação via `gh release create`.
#
# Uso: .\scripts\release.ps1

$ErrorActionPreference = 'Stop'

$RepoRoot    = Split-Path $PSScriptRoot -Parent
$VersionFile = Join-Path $RepoRoot 'VERSION'
$Version     = (Get-Content $VersionFile -Raw).Trim()
$Tag         = "v$Version"

Write-Host "==> Preparando release $Tag" -ForegroundColor Cyan

# -------------------------------------------------------------------------
# Validar estado do repositório
# -------------------------------------------------------------------------
Write-Host "`n--> Verificando repositorio"

$GitStatus = git -C $RepoRoot status --porcelain
if ($GitStatus) {
    Write-Error "Repositorio tem mudancas nao comitadas. Commite tudo antes de fazer release."
}

$ExistingTag = git -C $RepoRoot tag -l $Tag
if ($ExistingTag) {
    Write-Error "Tag '$Tag' ja existe. Atualize VERSION antes de tentar uma nova release."
}

# -------------------------------------------------------------------------
# Build
# -------------------------------------------------------------------------
Write-Host "`n--> Chamando build.ps1"
& (Join-Path $PSScriptRoot 'build.ps1')

# -------------------------------------------------------------------------
# Tag git
# -------------------------------------------------------------------------
Write-Host "`n--> Criando tag $Tag"
# git -C $RepoRoot tag -a $Tag -m "Release $Tag"
# git -C $RepoRoot push origin $Tag

# -------------------------------------------------------------------------
# GitHub Release
# -------------------------------------------------------------------------
Write-Host "`n--> Publicando GitHub Release $Tag"
# TODO Fase 7: usar `gh release create $Tag dist/installer/*.exe dist/*.mcpb --notes-file CHANGELOG.md`

Write-Host "`n==> Release $Tag pronto." -ForegroundColor Green
Write-Host "    Artefatos em: $(Join-Path $RepoRoot 'dist')"
