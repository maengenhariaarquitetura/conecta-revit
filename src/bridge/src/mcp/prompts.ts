import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import {
  ListPromptsRequestSchema,
  GetPromptRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { type LoadedSkill } from "../skills/index";
import { log } from "../log";

/**
 * Registra cada skill como MCP prompt "skill_<nome>" no servidor.
 *
 * Responsabilidades DESTE módulo (apenas exposição via MCP SDK):
 *   - prompts/list  → lista todos os prompts disponíveis
 *   - prompts/get   → devolve instructions.md como mensagem de contexto
 *
 * O carregamento e validação das skills ficam em skills/index.ts (sem SDK),
 * para que futuras tools (ex.: revit_load_skill) possam reutilizá-los.
 *
 * NUNCA use console.log aqui — stdout é exclusivo do protocolo JSON-RPC.
 */
export function registerPrompts(server: Server, skills: LoadedSkill[]): void {
  // ── prompts/list ─────────────────────────────────────────────────────────────
  server.setRequestHandler(ListPromptsRequestSchema, async () => ({
    prompts: skills.map((s) => ({
      name:        `skill_${s.name}`,
      description: s.description,
      arguments:   [],             // skills não recebem argumentos por enquanto
    })),
  }));

  // ── prompts/get ──────────────────────────────────────────────────────────────
  server.setRequestHandler(GetPromptRequestSchema, async (request) => {
    const promptName = request.params.name;
    const skill      = skills.find((s) => `skill_${s.name}` === promptName);

    if (!skill) {
      log.warn(`[prompts] Prompt desconhecido requisitado: '${promptName}'.`);
      throw new Error(`Prompt desconhecido: ${promptName}`);
    }

    log.info(
      `[prompts] Prompt invocado: '${promptName}' ` +
      `(source=${skill.source}, v${skill.version}).`
    );

    // O instructions.md entra como mensagem "user" para que o Claude receba
    // o conteúdo da skill como parte do contexto da conversa.
    return {
      description: skill.description,
      messages: [
        {
          role:    "user" as const,
          content: {
            type: "text" as const,
            text: skill.instructions,
          },
        },
      ],
    };
  });

  const names = skills.map((s) => `skill_${s.name}`).join(", ") || "(nenhum)";
  log.info(`[prompts] ${skills.length} prompt(s) MCP registrado(s): ${names}`);
}
