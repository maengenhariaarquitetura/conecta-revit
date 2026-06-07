; ConectaRevit â€” Inno Setup Script
; Gerado via scripts/build.ps1 apÃ³s etapa de build (ARCHITECTURE Â§ 9, Fase 6).
; TODO Fase 6: completar caminhos de arquivos, Ã­cone e lÃ³gica pÃ³s-instalaÃ§Ã£o.

#define AppName      "ConectaRevit"
#define AppVersion "0.1.0"
; build.ps1 substitui a linha acima por: #define AppVersion "0.1.0"
#define AppPublisher "Antigravity"
#define AppURL       "https://antigravity.com.br"

[Setup]
; AppId estÃ¡vel â€” nÃ£o alterar apÃ³s primeira distribuiÃ§Ã£o (mesmo GUID do add-in).
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
; LicenseFile=..\..\EULA-ptBR.md  ; TODO Fase 7: habilitar apÃ³s EULA final aprovada

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
; TODO Fase 6: adicionar DLLs do add-in, .addin, .mcpb e dependÃªncias do Roslyn.
; Source: "..\..\dist\addin\*"; DestDir: "{app}\addin"; Flags: ignoreversion recursesubdirs
; Source: "..\..\dist\conecta-revit.mcpb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ConectaRevit.Addin.dll"

[Run]
; TODO Fase 6: registrar o .addin no diretÃ³rio de add-ins do Revit e o .mcpb no Claude Desktop.
