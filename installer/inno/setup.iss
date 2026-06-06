; ConectaRevit — Inno Setup Script
; Gerado via scripts/build.ps1 após etapa de build (ARCHITECTURE § 9, Fase 6).
; TODO Fase 6: completar caminhos de arquivos, ícone e lógica pós-instalação.

#define AppName      "ConectaRevit"
#define AppVersion   "0.1.0"
; build.ps1 substitui a linha acima por: #define AppVersion "X.Y.Z"
#define AppPublisher "Antigravity"
#define AppURL       "https://antigravity.com.br"

[Setup]
; AppId estável — não alterar após primeira distribuição (mesmo GUID do add-in).
AppId={{a7f3c2e1-9b4d-4e6a-8c5f-2d1b3a6e9f04}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
OutputBaseFilename=ConectaRevit-Setup-{#AppVersion}
OutputDir=..\..\dist\installer
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; LicenseFile=..\..\EULA-ptBR.md  ; TODO Fase 7: habilitar após EULA final aprovada

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
; TODO Fase 6: adicionar DLLs do add-in, .addin, .mcpb e dependências do Roslyn.
; Source: "..\..\dist\addin\*"; DestDir: "{app}\addin"; Flags: ignoreversion recursesubdirs
; Source: "..\..\dist\conecta-revit.mcpb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ConectaRevit.Addin.dll"

[Run]
; TODO Fase 6: registrar o .addin no diretório de add-ins do Revit e o .mcpb no Claude Desktop.
