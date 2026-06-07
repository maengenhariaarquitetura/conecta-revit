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
        name: "revit_execute_code",
        description:
          "Executa código C# arbitrário no Autodesk Revit via a Revit API.\n\n" +

          "## Modos de execução\n" +
          "- **safe** (padrão): o harness abre e commita automaticamente uma Transaction em volta do código. " +
            "O script NÃO deve abrir Transaction própria — escreva as modificações diretamente " +
            "(ex.: `Wall.Create(doc, ...)`). Abrir Transaction dentro do script em Modo Seguro " +
            "causa erro de transação aninhada. " +
            "O harness já suprime warnings e erros do Revit automaticamente (IFailuresPreprocessor).\n" +
          "- **direct**: o script gerencia suas próprias transações. " +
            "OBRIGATÓRIO configurar `SetForcedModalHandling(false)` em cada Transaction para evitar " +
            "que warnings e erros do Revit abram caixas de diálogo modais e travem a execução:\n" +
            "```\n" +
            "using var tx = new Transaction(Doc, \"nome\");\n" +
            "var opts = tx.GetFailureHandlingOptions().SetForcedModalHandling(false);\n" +
            "tx.SetFailureHandlingOptions(opts);\n" +
            "tx.Start();\n" +
            "// ... modificações ...\n" +
            "tx.Commit();\n" +
            "```\n" +
            "Sem essa configuração, um simples warning de paredes sobrepostas pode travar o Revit.\n\n" +

          "## Globais disponíveis (sempre injetados)\n" +
          "- `Doc` — `Autodesk.Revit.DB.Document` ativo\n" +
          "- `UiDoc` — `Autodesk.Revit.UI.UIDocument` ativo\n" +
          "- `UiApp` — `Autodesk.Revit.UI.UIApplication`\n" +
          "- `Log(string msg)` — adiciona uma linha ao campo `log[]` do resultado\n\n" +

          "## Unidades\n" +
          "A Revit API trabalha internamente em **pés** (feet). " +
          "Converta de metros: `UnitUtils.ConvertToInternalUnits(valor, UnitTypeId.Meters)` " +
          "ou multiplique por `0.3048`.\n\n" +

          "## Namespaces já importados\n" +
          "`System`, `System.Linq`, `System.Collections.Generic`, " +
          "`Autodesk.Revit.DB`, `Autodesk.Revit.DB.Architecture`, `Autodesk.Revit.UI`.\n\n" +

          "## Valor de retorno\n" +
          "A última expressão do script é devolvida em `returnValue`. " +
          "Use `return <expr>;` para retornar explicitamente.",

        inputSchema: {
          type: "object" as const,
          properties: {
            code: {
              type: "string",
              description:
                "Código C# a executar.\n" +
                "Em Modo Seguro: escreva modificações diretamente, sem abrir Transaction " +
                "(ex.: `return Wall.Create(Doc, curve, levelId, false);`).\n" +
                "Em Modo Direto: gerencie Transaction com `using (var tx = new Transaction(Doc, \"nome\")) { ... }`.",
            },
            mode: {
              type: "string",
              enum: ["safe", "direct"],
              description:
                "Modo de execução. " +
                "safe = harness gerencia a Transaction (padrão). " +
                "direct = script gerencia a própria Transaction. " +
                "Omitir = usa o modo configurado no add-in (padrão: safe).",
            },
          },
          required: ["code"],
        },
      },
      {
        name: "revit_get_context",
        description:
          "Retorna o contexto atual do Revit: documento ativo, vista ativa, sistema de unidades e seleção atual.\n\n" +
          "Operação de leitura — não modifica o documento, não cria Transaction.\n\n" +
          "## Campos retornados\n" +
          "- `documentTitle` — título do documento ativo (null se nenhum documento aberto)\n" +
          "- `isFamilyDocument` — true se for um arquivo de família (.rfa)\n" +
          "- `activeViewId` / `activeViewName` / `activeViewType` — vista ativa\n" +
          "- `unitSystem` — `\"Metric\"` ou `\"Imperial\"` (baseado nas configurações do projeto)\n" +
          "- `selection` — lista de elementos selecionados: `{ id, category, name }`\n\n" +
          "## Uso típico\n" +
          "Chame antes de `revit_execute_code` para saber o sistema de unidades, o tipo de vista " +
          "ativa e os elementos selecionados pelo usuário.",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
        },
      },
      {
        name: "revit_revert_last",
        description:
          "Desfaz a última operação executada via `revit_execute_code` (equivalente a Ctrl+Z no Revit).\n\n" +
          "## Comportamento\n" +
          "- Usa `PostableCommand.Undo` — o Revit processa o Undo após retornar desta tool.\n" +
          "- `reverted: true` significa que o comando Undo foi postado com sucesso.\n" +
          "- `reverted: false` significa que não havia operação para desfazer (histórico vazio).\n" +
          "- `transactionName` — nome da última transação confirmada pelo harness (pode ser null).\n\n" +
          "## Limitações\n" +
          "- A operação é assíncrona: o Revit executa o Undo depois que esta chamada retorna.\n" +
          "- Desfaz a última transação do histórico do Revit — que pode não ser do ConectaRevit " +
          "se o usuário realizou outras ações manuais entre as chamadas.",
        inputSchema: {
          type: "object" as const,
          properties: {},
          required: [],
        },
      },
      // TODO Fase 3D: revit_run_tool, revit_list_tools
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

        // Sempre exibe as 3 listas (mesmo vazias) para o modelo saber o que foi afetado.
        lines.push(
          `Criados: [${result.elementsCreated.join(", ")}] | ` +
          `Modificados: [${result.elementsModified.join(", ")}] | ` +
          `Deletados: [${result.elementsDeleted.join(", ")}]`
        );

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

    if (name === "revit_get_context") {
      log.info("revit_get_context chamada.");
      const client = await getClient();

      if (!client) {
        return {
          content: [{
            type: "text" as const,
            text:
              "❌ Revit não conectado.\n\n" +
              "Verifique se o Revit está aberto e o botão 'Conectar' foi clicado.",
          }],
          isError: true,
        };
      }

      try {
        const r = await client.getContext();
        const lines: string[] = [];

        lines.push(`Documento: ${r.documentTitle ?? "(sem documento)"}`);
        if (r.isFamilyDocument) lines.push("Tipo: Família (.rfa)");
        lines.push(`Unidades: ${r.unitSystem}`);

        if (r.activeViewName)
          lines.push(`Vista ativa: ${r.activeViewName} (${r.activeViewType}, id=${r.activeViewId})`);
        else
          lines.push("Vista ativa: (nenhuma)");

        if (r.selection.length === 0) {
          lines.push("Seleção: (vazia)");
        } else {
          lines.push(`Seleção (${r.selection.length} elemento(s)):`);
          for (const s of r.selection)
            lines.push(`  id=${s.id}  categoria="${s.category}"  nome="${s.name}"`);
        }

        return { content: [{ type: "text" as const, text: lines.join("\n") }] };
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        log.error(`revit_get_context falhou: ${msg}`);
        return {
          content: [{ type: "text" as const, text: `Erro: ${msg}` }],
          isError: true,
        };
      }
    }

    if (name === "revit_revert_last") {
      log.info("revit_revert_last chamada.");
      const client = await getClient();

      if (!client) {
        return {
          content: [{
            type: "text" as const,
            text:
              "❌ Revit não conectado.\n\n" +
              "Verifique se o Revit está aberto e o botão 'Conectar' foi clicado.",
          }],
          isError: true,
        };
      }

      try {
        const r = await client.revertLast();
        if (!r.reverted) {
          return {
            content: [{ type: "text" as const, text: "Nada a desfazer (histórico vazio)." }],
          };
        }
        const txInfo = r.transactionName ? ` ("${r.transactionName}")` : "";
        return {
          content: [{ type: "text" as const, text: `✅ Undo postado${txInfo}. O Revit irá desfazer a última operação.` }],
        };
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        log.error(`revit_revert_last falhou: ${msg}`);
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
