/**
 * Loader de skills — ConectaRevit (ARCHITECTURE § 7).
 *
 * SEPARAÇÃO DE RESPONSABILIDADES (à prova de futuro):
 *   loadSkills()       — puro: varre, valida, retorna LoadedSkill[].
 *                        Sem dependência do MCP SDK. Reutilizável por tools futuras
 *                        (ex.: revit_load_skill) sem acoplamento ao protocolo MCP.
 *   registerPrompts()  — em mcp/prompts.ts: consome LoadedSkill[] e registra no SDK.
 *
 * PASTAS VARRIDAS (em ordem de precedência):
 *   1. skills-builtin/  — skills embarcadas no pacote (copiadas por copy-shared.js)
 *   2. %APPDATA%\ConectaRevit\skills\  — skills do usuário
 *   Skills do usuário sobrescrevem built-ins com o mesmo name.
 *
 * VALIDAÇÃO POR SKILL:
 *   - skill.json deve existir e ter { name: string, version: string, description: string }
 *   - instructions.md deve existir e não ser vazio
 *   - Skills inválidas são ignoradas com log; não derrubam a ponte.
 */

import * as fs   from "fs";
import * as path from "path";
import * as os   from "os";
import { log }   from "../log";

// ── Tipos públicos ────────────────────────────────────────────────────────────

export interface SkillJson {
  name:        string;
  version:     string;
  description: string;
  // Campos extras (revitTemplate, families, etc.) são ignorados pelo loader.
  [key: string]: unknown;
}

export interface LoadedSkill {
  /** Identificador canônico (vem de skill.json.name). */
  name:         string;
  version:      string;
  description:  string;
  /** Conteúdo completo de instructions.md — injetado como contexto no prompt. */
  instructions: string;
  /** "builtin" = embarcada no pacote; "user" = instalada pelo usuário. */
  source:       "builtin" | "user";
  /** Caminho absoluto da pasta da skill (para diagnóstico). */
  dir:          string;
}

// ── Resolução de caminhos ─────────────────────────────────────────────────────

/**
 * Pasta de skills embarcadas no pacote.
 *
 * Em runtime dentro do .mcpb:
 *   __dirname = <package>/skills/     (onde fica skills/index.js compilado)
 *   skills-builtin/ fica em <package>/skills-builtin/
 *
 * Em dev (node dist/index.js a partir de src/bridge/):
 *   __dirname = <bridge>/dist/skills/
 *   skills-builtin/ fica em <bridge>/dist/skills-builtin/ (copiado pelo copy-shared.js)
 */
function builtinSkillsDir(): string {
  return path.join(__dirname, "..", "skills-builtin");
}

/**
 * Pasta de skills do usuário: %APPDATA%\ConectaRevit\skills\
 * Fallback para os.homedir()/AppData/Roaming quando APPDATA não está no env
 * (mesmo padrão de resolveRuntimeJsonPath() em revit-client.ts).
 */
function userSkillsDir(): string {
  const appData =
    process.env["APPDATA"] ??
    path.join(os.homedir(), "AppData", "Roaming");
  return path.join(appData, "ConectaRevit", "skills");
}

// ── Funções internas ──────────────────────────────────────────────────────────

/**
 * Tenta carregar e validar a skill em `dir`.
 * Retorna null e emite log.warn se qualquer validação falhar.
 */
function loadOne(dir: string, source: "builtin" | "user"): LoadedSkill | null {
  const skillJsonPath    = path.join(dir, "skill.json");
  const instructionsPath = path.join(dir, "instructions.md");
  const rel              = path.basename(dir);   // para mensagens de log legíveis

  if (!fs.existsSync(skillJsonPath)) {
    log.warn(`[skills] Ignorando '${rel}': skill.json não encontrado em ${dir}.`);
    return null;
  }
  if (!fs.existsSync(instructionsPath)) {
    log.warn(`[skills] Ignorando '${rel}': instructions.md não encontrado em ${dir}.`);
    return null;
  }

  // Parsear skill.json
  let meta: SkillJson;
  try {
    meta = JSON.parse(fs.readFileSync(skillJsonPath, "utf8")) as SkillJson;
  } catch (err) {
    log.warn(`[skills] Ignorando '${rel}': skill.json inválido — ${err}`);
    return null;
  }

  // Validar campos obrigatórios
  if (!meta.name || typeof meta.name !== "string") {
    log.warn(`[skills] Ignorando '${rel}': skill.json sem campo 'name' (string).`);
    return null;
  }
  if (!meta.version || typeof meta.version !== "string") {
    log.warn(`[skills] Ignorando '${rel}': skill.json sem campo 'version' (string).`);
    return null;
  }
  if (!meta.description || typeof meta.description !== "string") {
    log.warn(`[skills] Ignorando '${rel}': skill.json sem campo 'description' (string).`);
    return null;
  }

  // Ler e validar instructions.md
  const instructions = fs.readFileSync(instructionsPath, "utf8").trim();
  if (!instructions) {
    log.warn(`[skills] Ignorando '${rel}': instructions.md existe mas está vazio.`);
    return null;
  }

  return {
    name:         meta.name,
    version:      meta.version,
    description:  meta.description,
    instructions,
    source,
    dir,
  };
}

/**
 * Varre a pasta `base` e retorna todas as skills válidas encontradas.
 * Subpastas são candidatas; arquivos soltos são ignorados silenciosamente.
 */
function scanDir(base: string, source: "builtin" | "user"): LoadedSkill[] {
  if (!fs.existsSync(base)) {
    log.info(`[skills] Pasta ${source} não encontrada (${base}) — nenhuma skill carregada daí.`);
    return [];
  }

  const loaded: LoadedSkill[] = [];
  let   skipped = 0;

  for (const entry of fs.readdirSync(base, { withFileTypes: true })) {
    if (!entry.isDirectory()) continue;
    const skill = loadOne(path.join(base, entry.name), source);
    if (skill) loaded.push(skill);
    else       skipped++;
  }

  log.info(
    `[skills] ${source}: ${loaded.length} válida(s), ${skipped} ignorada(s) em ${base}.`
  );
  return loaded;
}

// ── API pública ───────────────────────────────────────────────────────────────

/**
 * Carrega skills embarcadas + skills do usuário.
 * Skills do usuário têm precedência em conflito de nome.
 *
 * Não lança exceções — erros por skill são logados e a skill é ignorada.
 * Erros de leitura de pasta são tratados graciosamente.
 */
export function loadSkills(): LoadedSkill[] {
  const builtin = scanDir(builtinSkillsDir(), "builtin");
  const user    = scanDir(userSkillsDir(),    "user");

  // Merge com precedência para o usuário
  const map = new Map<string, LoadedSkill>();
  for (const s of builtin) map.set(s.name, s);
  for (const s of user)    map.set(s.name, s);   // sobrescreve builtin se conflito

  const all           = [...map.values()];
  const overrideCount = builtin.filter((b) => user.some((u) => u.name === b.name)).length;

  log.info(
    `[skills] Total: ${all.length} skill(s) carregada(s) ` +
    `(builtin=${builtin.length}, user=${user.length}` +
    (overrideCount > 0 ? `, user sobrescreve ${overrideCount} builtin(s)` : "") +
    `). Nomes: [${all.map((s) => s.name).join(", ") || "—"}]`
  );

  return all;
}
