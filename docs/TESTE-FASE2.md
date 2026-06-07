# Teste da Fase 2 — Hello-World Ponta a Ponta

Objetivo: provar o caminho **Claude Desktop → ponte MCP → add-in → resposta** sem tocar
na API do Revit. O add-in deve devolver `"Revit 2026"` (ou `"2025"`) como resultado.

---

## Pré-requisitos

| Requisito | Verificação |
|-----------|-------------|
| Revit 2025 ou 2026 instalado | `C:\Program Files\Autodesk\Revit 202X\Revit.exe` |
| Visual Studio 2022 com workload **.NET desktop development** | VS Installer |
| Node.js 20+ | `node --version` |
| npm 10+ | `npm --version` |
| Claude Desktop instalado e logado | [claude.ai/download](https://claude.ai/download) |

---

## Passo 1 — Compilar o add-in no Visual Studio

1. Abra `ConectaRevit.sln` na raiz do repositório.
2. Se o seu Revit não está em `C:\Program Files\Autodesk\Revit 2026`, altere a propriedade
   `RevitApiDir` antes de compilar:
   - Clique com o botão direito em **ConectaRevit.Addin** → **Propriedades** → aba **Build**
   - Ou passe via linha de comando:
     ```
     dotnet build src\addin\ConectaRevit.Addin\ConectaRevit.Addin.csproj ^
       -c Debug /p:RevitApiDir="C:\Program Files\Autodesk\Revit 2025"
     ```
3. Compile em **Debug**.
   - Saída esperada: `src\addin\ConectaRevit.Addin\bin\Debug\net8.0-windows\ConectaRevit.Addin.dll`

---

## Passo 2 — Instalar o add-in no Revit

Copie **dois arquivos** para o diretório de add-ins do usuário:

```
%APPDATA%\Autodesk\Revit\Addins\202X\
```

Substitua `202X` pela sua versão (2025 ou 2026).

**Arquivo 1 — manifesto:**
```
Copiar: src\addin\ConectaRevit.Addin\ConectaRevit.addin
Para:   %APPDATA%\Autodesk\Revit\Addins\202X\ConectaRevit.addin
```

**Arquivo 2 — DLL (e dependências):**
```
Copiar: src\addin\ConectaRevit.Addin\bin\Debug\net8.0-windows\*
Para:   C:\Program Files\Autodesk\Revit 202X\   (pasta do Revit)
        — OU —
        %APPDATA%\Autodesk\Revit\Addins\202X\   (mesmo diretório do .addin)
```

> **Nota sobre permissões:** Se o `HttpListener` reclamar de permissão (raro para 127.0.0.1),
> execute uma vez como Administrador:
> ```
> netsh http add urlacl url=http://127.0.0.1:8765/ user=EVERYONE
> ```
> Repita para as portas 8766–8775 se necessário.

---

## Passo 3 — Abrir o Revit e ligar o servidor

1. Abra o **Revit 2025/2026**.
   - Na primeira vez, o Revit perguntará se deseja carregar o add-in `ConectaRevit`.
     Clique em **Sempre Carregar**.
2. Você verá o painel **ConectaRevit** na aba **Add-ins** do ribbon.
3. Clique no botão **Conectar**.
   - Uma caixa de diálogo confirmará: *"Servidor conectado e aguardando a ponte MCP. Versão do Revit: 2026"*
4. Verifique que o arquivo foi criado:
   ```
   %APPDATA%\ConectaRevit\runtime.json
   ```
   Conteúdo esperado:
   ```json
   { "port": 8765, "pid": 12345, "protocolVersion": "1.0", "addinVersion": "0.1.0" }
   ```

---

## Passo 4 — Compilar e preparar a ponte

No terminal, a partir da raiz do repositório:

```bash
cd src\bridge
npm install
npm run build
```

Saída esperada: pasta `src\bridge\dist\` com `index.js` e demais arquivos JS.

---

## Passo 5 — Testar a ponte isoladamente (opcional mas recomendado)

Execute a ponte diretamente no terminal para validar antes de configurar o Claude Desktop:

```bash
cd src\bridge
node dist\index.js
```

**Critério de sucesso:** o processo fica rodando sem erros.  
**Em caso de falha:** verifique o arquivo `runtime.json` (Passo 3) e se o Revit está aberto com o servidor ligado.

Encerre com `Ctrl+C`.

---

## Passo 6 — Configurar o Claude Desktop

Abra o arquivo de configuração do Claude Desktop:

```
%APPDATA%\Claude\claude_desktop_config.json
```

Adicione (ou mescle) a entrada abaixo. Ajuste o caminho absoluto para `index.js`:

```json
{
  "mcpServers": {
    "conecta-revit": {
      "command": "node",
      "args": [
        "C:\\caminho\\absoluto\\para\\conecta-revit\\src\\bridge\\dist\\index.js"
      ]
    }
  }
}
```

> Substitua `C:\\caminho\\absoluto\\para\\conecta-revit` pelo caminho real do seu repositório.
> Use barras duplas `\\` no JSON do Windows.

Salve e **feche/reabra o Claude Desktop**.

---

## Passo 7 — Executar o hello-world no Claude Desktop

Na janela de chat do Claude Desktop:

1. Confirme que o MCP `conecta-revit` aparece no painel de ferramentas (ícone de chave).
2. Digite no chat:

   > Use a ferramenta `revit_execute_code` com o código: `"hello"`

   Ou mais explicitamente:

   > Execute código no Revit: `"hello"`

3. O Claude chamará a tool `revit_execute_code`.

---

## Critério de sucesso ✅

A resposta da tool deve conter:

```
Resultado: "Revit 2026"
```

(ou `"Revit 2025"` dependendo da versão instalada)

Isso prova que:
- A ponte MCP leu `runtime.json` ✓
- A ponte conectou via WebSocket ao add-in ✓
- O handshake validou o `protocolVersion` ✓
- O `execute_code` foi enfileirado e passou pelo `ExternalEvent` ✓
- O stub devolveu a versão do Revit ✓
- A ponte traduziu a resposta e devolveu ao Claude Desktop ✓

---

## Troubleshooting

| Sintoma | Causa provável | Solução |
|---------|---------------|---------|
| `runtime.json` não existe | Botão "Conectar" não foi clicado | Clicar "Conectar" no ribbon do Revit |
| `PROTOCOL_MISMATCH` | Ponte e add-in foram buildados de versões diferentes | Rebuild de ambos a partir do mesmo branch |
| Tool não aparece no Claude Desktop | `claude_desktop_config.json` incorreto | Verificar caminho absoluto e syntax JSON |
| `Cannot find module 'ws'` | `npm install` não rodou | Executar `npm install` em `src\bridge\` |
| Revit não mostra o painel "ConectaRevit" | `.addin` no diretório errado ou DLL não encontrada | Verificar caminhos do Passo 2 |
| `HttpListener` — acesso negado | Porta não registrada no Windows | Ver nota sobre `netsh` no Passo 2 |
