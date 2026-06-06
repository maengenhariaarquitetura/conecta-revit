# ConectaRevit — Arquitetura

> Fonte da verdade do projeto. Claude Code deve seguir este documento. Em caso de conflito com qualquer outra instrução, este arquivo + `PROTOCOL.md` prevalecem.

## 1. O que é

Plugin que liga o **Autodesk Revit** ao **Claude Desktop**, permitindo que o Claude execute **qualquer operação suportada pela API do Revit**, usando os **créditos da assinatura** do usuário (Pro/Max) — nunca a API paga.

Diferencial frente aos concorrentes: não há lista fechada de funções. O motor executa código arbitrário contra a API do Revit, com um switch de segurança.

## 2. Restrições inegociáveis

- Roda **dentro do Claude Desktop** (consumo = assinatura, não API).
- Distribuição **plug-and-play**: usuário leigo instala um `.exe` e clica em um botão.
- Suporte inicial: **Revit 2025 e 2026** (ambos .NET 8). Não suportar versões ≤ 2024 (são .NET Framework 4.8).
- Plataforma: **Windows** (Revit é Windows-only).
- Idioma da interface e EULA: **pt-BR**.
- Sem trava conceitual: tudo que a API do Revit permite, o Claude pode fazer.
- **Licença obrigatória** (ativação por chave) já no MVP.

## 3. Topologia (3 peças + 1 contrato)

```
Claude Desktop (chat, assinatura)
      ⇅  stdio  (protocolo MCP)
Ponte MCP        — Node/TypeScript, distribuída como .mcpb (1 clique)
      ⇅  WebSocket localhost  (contrato em PROTOCOL.md)
Add-in do Revit  — C# .NET 8, dentro do Revit.exe  (ribbon + servidor + execução)
      ⇅  ExternalEvent → thread principal → API do Revit (Transaction)
```

**Por que a ponte existe e não conectamos o Revit direto ao Claude:** a API do Revit só roda dentro do processo `Revit.exe`. A ponte stdio é o que o Claude Desktop sabe lançar (formato MCPB). A ponte apenas repassa mensagens; toda a lógica de Revit vive no add-in.

## 4. Stack

| Peça | Tecnologia | Observações |
|---|---|---|
| Add-in | C# / .NET 8 / `net8.0-windows` | Referência a `RevitAPI.dll` + `RevitAPIUI.dll` (CopyLocal=false). |
| Servidor WS | `System.Net.HttpListener` + `System.Net.WebSockets` (nativo) | Evitar libs externas dentro do Revit (conflito de assembly). |
| Motor de código | `Microsoft.CodeAnalysis.CSharp.Scripting` (Roslyn) | Única dependência pesada do add-in. Cuidar do AssemblyLoadContext. |
| Ponte | Node 20+/TypeScript + `@modelcontextprotocol/sdk` | Empacotada via `@anthropic-ai/mcpb` (`mcpb pack`). |
| Installer | Inno Setup (`ISCC.exe`) | Gera `.exe`. (WiX descartado: OSMF cobra de quem gera receita.) |
| Licença | Endpoint na Supabase/Vercel existente do usuário | Reusar infra; não montar serviço novo. |

## 5. Add-in — componentes internos

### 5.1 Ribbon (`Ribbon/`)
6 botões, painel "ConectaRevit":
1. **Conectar / Desconectar** — liga/desliga o servidor WS. Valida licença antes de ligar.
2. **Status** — exibe estado (ligado/desligado, porta, doc ativo).
3. **Verificar Requisitos** — checa Claude Desktop instalado, assinatura, porta livre, .mcpb registrado. Mata a maior parte do suporte.
4. **Console** — janela com o log em tempo real do que o Claude executou.
5. **Reverter última ação** — desfaz a última ação do Claude (ver 5.4).
6. **Configurações** — switch Modo Seguro/Direto, porta, chave de licença.

### 5.2 Servidor (`Server/`)
- Hospeda WebSocket em `localhost`, porta default **8765**. Se ocupada, tenta 8766…8775 e usa a primeira livre.
- Ao subir, grava `%AppData%\ConectaRevit\runtime.json` = `{ port, pid, protocolVersion, addinVersion }`. A ponte lê esse arquivo para descobrir a porta (não hardcodar porta).
- Roda em thread de background. **Nunca chama a API do Revit direto** — sempre via ExternalEvent (5.3).
- Processa requisições **serialmente** (fila). Timeout por requisição: 120s. Se chega requisição com uma em andamento → enfileira (profundidade máx. 10; acima disso devolve erro `BUSY`).

### 5.3 Execução na thread principal (`Execution/`)
A API do Revit exige contexto válido (thread principal). Padrão obrigatório:

```
WS recebe request (thread bg)
  → enfileira o job + cria TaskCompletionSource
  → externalEvent.Raise()
Revit chama IExternalEventHandler.Execute(UIApplication) na thread principal
  → desenfileira, executa, resolve o TaskCompletionSource
WS aguarda o TCS e responde
```

- `ExternalEvent.Create(handler)` deve ocorrer em `OnStartup` (contexto de API válido).
- Um único `ExternalEvent`/handler com fila interna. Não criar ExternalEvent por requisição.

### 5.4 Modos de segurança (switch) + Transações
Configuração persistente. Default = **Seguro**.

- **Modo Seguro:** o harness abre uma `Transaction` (ou `TransactionGroup` nomeado `ConectaRevit: <resumo>`) **antes** de rodar o código do Claude e faz commit no sucesso / **rollback automático** em qualquer exceção. O código do Claude **não deve** abrir transação própria (a instrução injetada na ferramenta avisa isso). Modelo nunca fica num estado parcial.
- **Modo Direto:** o harness **não** envelopa. O código do Claude gerencia suas próprias transações (`using (var t = new Transaction(doc, "..."))`). Permite operações multi-transação, regeneração entre passos, transaction groups. Para usuários avançados.
- **Reverter última ação:** cada ação do Claude vira um `TransactionGroup` nomeado. O botão dispara o Undo nativo via `RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)` + `UIApplication.PostCommand`. (Não existe undo programático direto de transação já commitada; o Undo nativo é o caminho pragmático.)

### 5.5 Motor Roslyn
- `CSharpScript` recebe globals: `Document Doc`, `UIDocument UiDoc`, `UIApplication UiApp`, `Action<string> Log`, e o helper `Tools` (5.6).
- `ScriptOptions` com referências a `typeof(Document).Assembly`, `typeof(UIDocument).Assembly`, `System`, `System.Linq`, e imports dos namespaces `Autodesk.Revit.DB`, `Autodesk.Revit.UI`, `System.Linq`, `System.Collections.Generic`.
- Retorno do script é serializado e devolvido (ver `execute_code` em PROTOCOL.md).
- Erro de compilação → `COMPILATION_ERROR` com a lista de diagnósticos. Erro de runtime → `RUNTIME_ERROR` com stack resumido.
- **Risco conhecido:** carregamento de assembly no Revit 2025+ (.NET 8) é sensível. Manter as DLLs na pasta própria do add-in e não duplicar versões que o Revit já carrega.

### 5.6 Ferramentas de alto nível (`Tools/`)
Hibridismo: além do código arbitrário, operações comuns são pré-compiladas (mais rápidas e seguras que recompilar via Roslyn).

- Interface `IRevitTool { string Name; string Description; JsonSchema InputSchema; object Execute(ToolContext ctx, JObject args); }`.
- Registradas automaticamente por reflection no startup.
- Expostas de duas formas: (a) como tool MCP `run_tool` (Claude chama direto, sem escrever código); (b) acessíveis de dentro do código Roslyn via o helper `Tools`.
- MVP: começar com 3–5 (ex.: `create_wall`, `get_selection_info`, `set_parameter`, `list_categories`). Lista cresce sem mexer na arquitetura.

### 5.7 Licença (`Licensing/`)
- Chave guardada nas configurações. Na ação **Conectar**: POST ao endpoint Supabase `/functions/v1/validate-license` com `{ key, machineId, productVersion }` → `{ valid, plan, expiresAt }`.
- `machineId` = hash estável de identificador de hardware (enforcement de seat).
- Cacheia o resultado; **grace period offline de 7 dias**. Chave inválida/expirada → botão Conectar bloqueado com mensagem.
- Cobrança (Mercado Pago Assinaturas) é externa ao app: por ora emite-se chave manual; integração de billing depois. Não travar o MVP nisso.

### 5.8 Configurações (`Settings/`)
Persistidas em `%AppData%\ConectaRevit\settings.json`: `mode` (`safe`|`direct`), `port`, `licenseKey`, `language`.

## 6. Ponte MCP (`bridge/`)

- Servidor MCP via stdio (lançado pelo Claude Desktop). Usa `@modelcontextprotocol/sdk`.
- Ao iniciar: lê `runtime.json`, abre WebSocket cliente para o add-in, faz `handshake` (valida versão de protocolo — major incompatível = recusa com mensagem clara).
- Traduz chamadas MCP ↔ mensagens do PROTOCOL.md.
- Tools MCP expostas: `revit_execute_code`, `revit_get_context`, `revit_run_tool`, `revit_list_tools`, `revit_revert_last`.
- Reexpede eventos `log`/`status` do add-in como notificações/atualizações de contexto.
- Empacotada em `.mcpb` com `manifest.json` (nome, versão, descrição, ícone 512x512, comando de start).

## 7. Skills (skill packs) — desde o MVP

Skill = especialização carregável (ex.: marcenaria) ligando instrução + template.

Estrutura `skills/<nome>/`:
```
skill.json        { name, version, description, revitTemplate?, families?[] }
instructions.md   conhecimento/regras da especialidade (vira contexto no Claude)
template/         (opcional) refs de template, naming, tipos
```

- A ponte varre **duas** pastas: as embarcadas (`bridge/skills/`) e as do usuário (`%AppData%\ConectaRevit\skills\`).
- Cada skill é registrada como **MCP prompt** `skill_<nome>`; ao ser invocada no Claude Desktop, injeta o `instructions.md` como contexto.
- Distribuição: uma skill é só uma pasta zipável que o cliente joga em `%AppData%\ConectaRevit\skills\`. Vendável por vertical.

## 8. Versionamento

- **Semver único** para o produto inteiro: `MAJOR.MINOR.PATCH` no arquivo `VERSION` na raiz.
- `build.ps1` carimba as 3 peças a partir do `VERSION`: `AssemblyInfo` do add-in, `package.json` + `manifest.json` da ponte.
- **Nunca** versionar as peças separadamente — fonte garantida de incompatibilidade em campo. A compatibilidade real entre ponte e add-in é garantida pelo `protocolVersion` (PROTOCOL.md), não pelo semver do produto.
- Tag git por release + `CHANGELOG.md`.

## 9. Fluxo de build/release (resumo)

1. `build.ps1` carimba versão.
2. Compila add-in (Release, net8.0-windows) → DLLs + `.addin`.
3. `npm run build` na ponte → JS + `mcpb pack` → `.mcpb`.
4. `ISCC.exe installer\inno\setup.iss` → `ConectaRevit-Setup-x.y.z.exe`.
5. Assina o `.exe` (Fase 7).
6. Tag + release no GitHub.

## 10. Sequência de implementação (ordem obrigatória)

1. **Scaffold** (Fase 1) — árvore, docs, `.gitignore`, esqueletos, manifests, `build.ps1`.
2. **Contrato primeiro** (Fase 2) — `PROTOCOL.md` + tipos espelhados em `shared/`; caminho hello-world com stub (Claude Desktop → ponte → add-in stub devolve versão do Revit). **Antes de qualquer lógica de Revit.**
3. **Add-in core** (Fase 3, Visual Studio) — ribbon → servidor WS → ExternalEvent/Transaction → switch de modo → Roslyn → ferramentas. Pronto = "crie uma parede de 3m" funciona pelo Claude Desktop.
4. **Ponte completa + skills** (Fase 4) — tools/prompts/resources MCP, loader de skills, `.mcpb`, skill `marcenaria`.
5. **Licença** (Fase 5).
6. **Installer Inno** (Fase 6).
7. **EULA + assinatura + release** (Fase 7).
