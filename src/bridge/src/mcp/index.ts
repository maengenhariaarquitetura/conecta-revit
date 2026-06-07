import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { RevitClient } from "../revit-client";
import { log } from "../log";

/**
 * Registra as tools MCP no servidor.
 * Fase 2: apenas revit_execute_code (PROTOCOL.md § 7 — hello-world mínimo).
 * Outras 4 tools: TODO Fase 3.
 *
 * @param getClient  Função assíncrona que retorna o cliente ativo ou null.
 *                   É chamada a cada invocação de tool (lazy connect):
 *                   se não houver cliente, tenta conectar naquele instante
 *                   e só retorna null se a tentativa realmente falhar.
 *
 * NUNCA use console.log aqui — stdout é exclusivo do protocolo JSON-RPC.
 */
export function registerTools(
  server: Server,
  getClient: () => Promise<RevitClient | null>
): void {
  // ── Lista de tools disponíveis ──────────────────────────────────────────
  server.setRequestHandler(ListToolsRequestSchema, async () => ({
    tools: [
      {
        name:        "revit_execute_code",
        description: "Executa código C# no Revit via a API do Revit. " +
                     "Em modo seguro (padrão), o código NÃO deve abrir transação própria — " +
                     "o harness envelopa automaticamente. " +
                     "Retorna o valor da última expressão do script.",
        inputSchema: {
          type: "object" as const,
          properties: {
            code: {
              type:        "string",
              description: "Código C# a executar. Globals disponíveis: Doc, UiDoc, UiApp, Log(string).",
            },
            mode: {
              type:        "string",
              enum:        ["safe", "direct"],
              description: "Modo de execução. Omitir = usa o modo configurado no add-in (default: safe).",
            },
          },
          required: ["code"],
        },
      },
      // TODO Fase 3: revit_get_context
      // TODO Fase 3: revit_run_tool
      // TODO Fase 3: revit_list_tools
      // TODO Fase 3: revit_revert_last
    ],
  }));

  // ── Execução de tool ────────────────────────────────────────────────────
  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;

    if (name === "revit_execute_code") {
      const code = args?.["code"];
      const mode = args?.["mode"] as string | undefined;

      if (typeof code !== "string" || code.trim() === "") {
        return {
          content: [{ type: "text" as const, text: "Erro: o argumento 'code' é obrigatório e não pode ser vazio." }],
          isError: true,
        };
      }

      // Lazy connect: tenta (re)conectar ao add-in no momento da chamada.
      // Se já está conectado, retorna o cliente existente imediatamente.
      // Se não, lê runtime.json + abre WS + faz handshake agora.
      log.info(`revit_execute_code chamada. Verificando conexão ao add-in…`);
      const client = await getClient();

      if (!client) {
        log.warn("revit_execute_code: add-in não disponível após tentativa de conexão.");
        return {
          content: [{
            type: "text" as const,
            text:
              "❌ Revit não conectado.\n\n" +
              "A ponte tentou conectar agora e falhou. Verifique:\n" +
              "1. O Autodesk Revit está aberto?\n" +
              "2. O botão 'Conectar' no painel ConectaRevit foi clicado?\n" +
              "3. O arquivo %APPDATA%\\ConectaRevit\\runtime.json existe?\n\n" +
              "Após conectar no Revit, tente esta tool novamente.",
          }],
          isError: true,
        };
      }

      log.debug(`revit_execute_code [mode=${mode ?? "default"}] len=${code.length}`);

      try {
        const result = await client.executeCode({ code, mode: mode as "safe" | "direct" | undefined });

        const lines: string[] = [];

        if (result.returnValue !== null && result.returnValue !== undefined)
          lines.push(`Resultado: ${JSON.stringify(result.returnValue)}`);

        if (result.log.length > 0)
          lines.push("Log:\n" + result.log.map((l) => `  ${l}`).join("\n"));

        if (result.transactionName)
          lines.push(`Transação: ${result.transactionName}`);

        if (result.elementsCreated.length > 0)
          lines.push(`Elementos criados: ${result.elementsCreated.join(", ")}`);

        return {
          content: [{ type: "text" as const, text: lines.join("\n") || "(sem resultado)" }],
        };
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        log.error(`revit_execute_code falhou: ${msg}`);
        return {
          content: [{ type: "text" as const, text: `Erro: ${msg}` }],
          isError: true,
        };
      }
    }

    return {
      content: [{ type: "text" as const, text: `Tool desconhecida: ${name}` }],
      isError: true,
    };
  });
}
