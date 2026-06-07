import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { RevitClient } from "./revit-client";
import { registerTools } from "./mcp/index";
import { loadSkills } from "./skills/index";
import { log } from "./log";

/**
 * Ponto de entrada da ponte MCP (ARCHITECTURE § 6).
 *
 * ORDEM DE INICIALIZAÇÃO (crítico para stdio MCP):
 *   1. Servidor MCP sobe e conecta ao transporte stdio PRIMEIRO.
 *      → Claude Desktop já pode enviar initialize e receber resposta.
 *   2. ensureConnected() é chamada lazy: na inicialização E cada vez que
 *      revit_execute_code for invocada sem cliente ativo.
 *   3. Auto-reconexão com backoff exponencial quando o socket cai.
 *
 * NUNCA use console.log neste arquivo — stdout é exclusivo do protocolo JSON-RPC.
 * Todo log vai para stderr via log.ts.
 */

// ── Estado da conexão Revit ───────────────────────────────────────────────────

/** Cliente ativo. null = não conectado. */
let revitClient: RevitClient | null = null;

/**
 * Promise de conexão em andamento — evita tentativas concorrentes.
 * Qualquer chamada a ensureConnected() enquanto uma tentativa está em curso
 * aguarda a mesma Promise, sem disparar nova conexão.
 */
let _connectPromise: Promise<RevitClient | null> | null = null;

/** Delay do próximo retry de reconexão automática (ms). */
let _reconnectDelay = 5_000;
const RECONNECT_MAX = 60_000;

// ── Reconexão automática ──────────────────────────────────────────────────────

function scheduleReconnect(): void {
  log.info(`Reconexão automática em ${_reconnectDelay / 1000} s…`);
  setTimeout(async () => {
    const client = await ensureConnected();
    if (!client) {
      // Ainda não conectou — aumenta o backoff e tenta de novo.
      _reconnectDelay = Math.min(_reconnectDelay * 2, RECONNECT_MAX);
      scheduleReconnect();
    }
    // Se conectou, attachClientEvents registrou os handlers; backoff resetado lá.
  }, _reconnectDelay);
}

// ── Eventos do cliente ────────────────────────────────────────────────────────

function attachClientEvents(client: RevitClient): void {
  client.on("status", (data: unknown) => {
    log.info(`[addin] status: ${JSON.stringify(data)}`);
  });

  client.on("close", () => {
    log.warn("Conexão com o add-in encerrada. Iniciando reconexão automática…");
    revitClient = null;
    _reconnectDelay = 5_000; // reseta backoff — próxima tentativa rápida
    scheduleReconnect();
  });

  client.on("error", (err: Error) => {
    log.error(`Erro na conexão WS: ${err.message}`);
  });

  // TODO Fase 4: reexpedir log/document_changed como notificações MCP
  client.on("log", (data: unknown) => {
    log.info(`[addin-log] ${JSON.stringify(data)}`);
  });

  client.on("document_changed", (data: unknown) => {
    log.info(`[document_changed] ${JSON.stringify(data)}`);
  });
}

// ── Conexão principal (lazy, protegida contra concorrência) ───────────────────

async function doConnect(): Promise<RevitClient | null> {
  log.info("Tentando conectar ao add-in do Revit…");
  try {
    const client = await RevitClient.connect();
    revitClient = client;
    _reconnectDelay = 5_000; // conexão bem-sucedida: reseta backoff
    attachClientEvents(client);
    log.info("✓ Conectado ao add-in do Revit.");
    return client;
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    log.warn(`Falha ao conectar ao add-in: ${msg}`);
    return null;
  }
}

/**
 * Garante que o cliente Revit está conectado.
 *
 * - Se já está conectado, retorna imediatamente.
 * - Se uma tentativa está em andamento, aguarda ela (sem duplicar).
 * - Caso contrário, inicia uma nova tentativa.
 *
 * Chamada tanto na inicialização quanto em cada invocação de tool
 * enquanto revitClient === null (lazy connect).
 */
export async function ensureConnected(): Promise<RevitClient | null> {
  if (revitClient) return revitClient;
  if (_connectPromise) return _connectPromise;

  _connectPromise = doConnect().finally(() => {
    _connectPromise = null;
  });

  return _connectPromise;
}

// ── Entrada principal ─────────────────────────────────────────────────────────

async function main(): Promise<void> {
  // ── 1. Servidor MCP via stdio — sobe ANTES de qualquer conexão ao Revit ──
  //    Subir depois significaria: Claude Desktop envia initialize durante o
  //    await de RevitClient.connect() e interpreta a demora como "process exiting early".
  const server = new Server(
    { name: "conecta-revit", version: "0.1.0" },
    { capabilities: { tools: {} } }
  );

  // loadSkills() é síncrono e rápido (leitura de arquivos locais).
  const skills = loadSkills();

  // getClient é assíncrono: tenta conectar na hora se não estiver conectado.
  registerTools(server, ensureConnected, skills);

  const transport = new StdioServerTransport();
  await server.connect(transport);
  log.info("Servidor MCP conectado via stdio — aguardando requisições.");

  // ── 2. Tentativa inicial em background (não bloqueia o servidor MCP) ──────
  //    Se falhar, ensureConnected() tentará de novo na primeira chamada de tool.
  ensureConnected();
}

main().catch((err) => {
  log.error(`Erro fatal na inicialização: ${err}`);
  process.exit(1);
});
