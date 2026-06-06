# Release Checklist

> TODO Fase 7: preencher com o processo completo de release.

## Pré-release

- [ ] Versão em `VERSION` atualizada (semver único — ARCHITECTURE § 8)
- [ ] `CHANGELOG.md` atualizado com todas as mudanças
- [ ] Build limpo e sem warnings (`scripts/build.ps1`)
- [ ] Testes passando

## Build & Empacotamento

- [ ] `scripts/build.ps1` executa sem erros
- [ ] `.mcpb` gerado em `dist/`
- [ ] Installer `.exe` gerado em `dist/installer/`

## Validação

- [ ] Installer testado em máquina Windows limpa (sem Revit pré-instalado)
- [ ] Add-in carrega no Revit 2025
- [ ] Add-in carrega no Revit 2026
- [ ] Hello-world Claude Desktop → ponte → add-in funcionando

## Publicação

- [ ] Assinatura digital aplicada ao `.exe` (Fase 7)
- [ ] Tag git criada: `vX.Y.Z`
- [ ] GitHub Release publicado com `.exe`, `.mcpb` e notas do CHANGELOG
