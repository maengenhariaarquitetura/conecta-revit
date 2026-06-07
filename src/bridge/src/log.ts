/**
 * Logger central do ConectaRevit Bridge.
 *
 * REGRA: stdout é EXCLUSIVO do transporte JSON-RPC (SDK MCP).
 * Todo e qualquer log da ponte deve ir para process.stderr, jamais para stdout.
 * Nunca use console.log neste projeto — ele escreve em stdout e corrompe o protocolo.
 *
 * Uso:
 *   import { log } from "./log";
 *   log.info("mensagem");
 *   log.warn("aviso");
 *   log.error("erro");
 *   log.debug("detalhe");   // útil durante desenvolvimento
 */

const PREFIX = "[ConectaRevit]";

function now(): string {
  return new Date().toISOString();
}

function write(level: string, msg: string): void {
  // process.stderr.write é síncrono e nunca interfere com stdout/JSON-RPC.
  process.stderr.write(`${now()} ${PREFIX} ${level.padEnd(5)} ${msg}\n`);
}

export const log = {
  info:  (msg: string): void => write("INFO",  msg),
  warn:  (msg: string): void => write("WARN",  msg),
  error: (msg: string): void => write("ERROR", msg),
  debug: (msg: string): void => write("DEBUG", msg),
};
