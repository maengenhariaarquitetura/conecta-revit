# ConectaRevit — PROTOCOL

> Contrato de comunicação entre a **Ponte MCP** (cliente) e o **Add-in do Revit** (servidor) sobre WebSocket em `localhost`. Fonte da verdade da Fase 2. Os tipos em `shared/Protocol.cs` e `shared/protocol.ts` devem espelhar exatamente este documento.

## 0. Versão do protocolo

`protocolVersion` atual: **"1.0"**.

Formato `MAJOR.MINOR` (string). Regra de compatibilidade:
- **MAJOR diferente** entre ponte e add-in → conexão **recusada** com erro `PROTOCOL_MISMATCH` e mensagem ao usuário ("Atualize o ConectaRevit").
- **MINOR diferente** → permitido (compatível; o lado mais novo degrada graciosamente).

Esta verificação acontece no `handshake`, antes de qualquer outra mensagem.

## 1. Transporte

- WebSocket, texto (JSON UTF-8), uma mensagem JSON por frame.
- Add-in = servidor; ponte = cliente.
- Porta descoberta via `%AppData%\ConectaRevit\runtime.json` (`{ port, pid, protocolVersion, addinVersion }`). A ponte não hardcoda porta.
- Sem TLS (loopback). O servidor só aceita conexões de `127.0.0.1`/`::1`.

## 2. Envelopes

Três tipos de mensagem.

### 2.1 Request (ponte → add-in)
```json
{ "id": "uuid-v4", "method": "string", "params": { } }
```

### 2.2 Response (add-in → ponte) — correlacionada por `id`
Sucesso:
```json
{ "id": "uuid-v4", "ok": true, "result": { } }
```
Erro:
```json
{ "id": "uuid-v4", "ok": false, "error": { "code": "STRING_CODE", "message": "humano, pt-BR", "details": "opcional" } }
```

### 2.3 Event (add-in → ponte) — sem `id`, sem resposta
```json
{ "event": "log" | "status" | "document_changed", "data": { } }
```

Regras:
- Todo `id` é UUID v4 gerado pela ponte; o add-in ecoa o mesmo `id`.
- A ponte deve tratar respostas fora de ordem (correlacionar por `id`).
- Timeout do lado da ponte: 125s (um pouco acima do timeout do add-in, 120s).

## 3. Métodos (request/response)

### 3.1 `handshake`
Primeira mensagem após conectar.

params:
```json
{ "protocolVersion": "1.0", "bridgeVersion": "x.y.z" }
```
result:
```json
{
  "protocolVersion": "1.0",
  "addinVersion": "x.y.z",
  "revitVersion": "2026",
  "documentTitle": "Projeto1.rvt" | null,
  "mode": "safe" | "direct"
}
```
Erros: `PROTOCOL_MISMATCH`.

### 3.2 `ping`
params: `{}` · result: `{ "pong": true, "ts": 1730000000000 }`
Usado como keepalive (a ponte envia a cada 30s).

### 3.3 `get_context`
Leitura do estado atual. Não modifica nada (não abre transação).

params: `{}`
result:
```json
{
  "documentTitle": "Projeto1.rvt" | null,
  "isFamilyDocument": false,
  "activeViewId": 123456,
  "activeViewName": "Nível 1",
  "activeViewType": "FloorPlan",
  "unitSystem": "Metric",
  "selection": [ { "id": 987, "category": "Walls", "name": "Parede Básica 200mm" } ]
}
```
Erros: `NO_DOCUMENT` (se nenhum documento aberto).

### 3.4 `execute_code`
Executa código C# arbitrário (Roslyn) na thread principal.

params:
```json
{ "code": "string C#", "mode": "safe" | "direct" | null }
```
- `mode` ausente/null → usa o modo configurado no add-in.
- Globals disponíveis ao código: `Doc` (`Document`), `UiDoc` (`UIDocument`), `UiApp` (`UIApplication`), `Log(string)` (`Action<string>`), `Tools` (helper das ferramentas de alto nível).
- **Modo seguro:** o código NÃO deve abrir transação; o harness envelopa em `TransactionGroup` e faz rollback automático em exceção.
- **Modo direto:** o código gerencia as próprias transações.
- O valor retornado pelo script (última expressão) é serializado em `returnValue` (best-effort: tipos primitivos e coleções viram JSON; objetos complexos viram `ToString()`).

result:
```json
{
  "returnValue": <json> | null,
  "log": [ "linha 1", "linha 2" ],
  "transactionName": "ConectaRevit: <resumo>" | null,
  "elementsCreated": [123, 124] 
}
```
Erros: `NO_DOCUMENT`, `COMPILATION_ERROR` (com `details` = diagnósticos), `RUNTIME_ERROR` (com `details` = stack resumido), `TRANSACTION_FAILED`, `API_CONTEXT_ERROR`, `TIMEOUT`, `BUSY`.

### 3.5 `run_tool`
Executa uma ferramenta de alto nível pré-compilada.

params: `{ "tool": "create_wall", "args": { } }`
result: `{ "returnValue": <json>, "log": [ ], "elementsCreated": [ ] }`
Erros: `UNKNOWN_TOOL`, `INVALID_ARGS`, + os mesmos de `execute_code`.

### 3.6 `list_tools`
params: `{}`
result:
```json
{ "tools": [ { "name": "create_wall", "description": "...", "inputSchema": { } } ] }
```

### 3.7 `revert_last`
Desfaz a última ação do Claude via Undo nativo do Revit.

params: `{}`
result: `{ "reverted": true, "transactionName": "ConectaRevit: <resumo>" | null }`
Erros: `NOTHING_TO_REVERT`, `API_CONTEXT_ERROR`.

## 4. Eventos (add-in → ponte)

### 4.1 `log`
```json
{ "event": "log", "data": { "level": "info"|"warn"|"error", "message": "string", "ts": 1730000000000 } }
```
Emitido durante execução (inclui as chamadas a `Log(...)`). A ponte repassa ao Console e ao contexto MCP.

### 4.2 `status`
```json
{ "event": "status", "data": { "connected": true, "documentTitle": "Projeto1.rvt"|null, "mode": "safe"|"direct" } }
```
Emitido em mudanças de estado (conectar, trocar de modo, etc.).

### 4.3 `document_changed`
```json
{ "event": "document_changed", "data": { "documentTitle": "Outro.rvt"|null } }
```
Emitido quando o usuário troca o documento ativo no Revit.

## 5. Códigos de erro

| code | significado |
|---|---|
| `PROTOCOL_MISMATCH` | MAJOR de protocolo incompatível. |
| `LICENSE_INVALID` | Sem licença válida (a conexão nem deveria subir; defesa em profundidade). |
| `NO_DOCUMENT` | Nenhum documento Revit aberto. |
| `API_CONTEXT_ERROR` | Falha ao obter contexto válido da API (ExternalEvent). |
| `COMPILATION_ERROR` | Código C# não compilou (`details` = diagnósticos). |
| `RUNTIME_ERROR` | Exceção durante execução (`details` = stack resumido). |
| `TRANSACTION_FAILED` | Falha ao commitar/rollback (modo seguro). |
| `UNKNOWN_TOOL` | Ferramenta inexistente em `run_tool`. |
| `INVALID_ARGS` | Args não batem com o schema da ferramenta. |
| `NOTHING_TO_REVERT` | Sem ação do Claude para desfazer. |
| `TIMEOUT` | Execução excedeu 120s. |
| `BUSY` | Fila cheia (>10 requisições pendentes). |

## 6. Ordem de uso (caminho feliz)

```
1. ponte conecta WS  →  2. handshake  →  (valida protocolVersion)
3. get_context        →  (Claude sabe o estado)
4. execute_code / run_tool  (N vezes)  ↔  eventos log/status
5. revert_last (se pedido)
6. ping a cada 30s (mantém vivo)
```

## 7. Hello-world da Fase 2 (alvo mínimo)

Implementar SOMENTE: `handshake` + `ping` + um `execute_code` **stub** no add-in que ignora o código e devolve `{ returnValue: "Revit <versão>", log: [] }`. Sem Roslyn, sem transação. Objetivo: provar o caminho Claude Desktop → ponte → add-in → resposta de ponta a ponta antes de tocar na API do Revit.
