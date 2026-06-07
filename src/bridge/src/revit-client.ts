import { EventEmitter } from "events";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { randomUUID } from "crypto";
import WebSocket from "ws";
// Importa de ./shared/protocol.ts — cópia gerada pelo prebuild (copy-shared.js).
// Fonte da verdade: shared/protocol.ts na raiz do repositório. Não editar este import.
import {
  PROTOCOL_VERSION,
  HandshakeParams,
  HandshakeResult,
  ExecuteCodeParams,
  ExecuteCodeResult,
} from "./shared/protocol";
import { log } from "./log";

// ─── Constantes ──────────────────────────────────────────────────────────────

const PING_INTERVAL_MS   = 30_000;  // keepalive a cada 30 s (PROTOCOL.md § 3.2)
const REQUEST_TIMEOUT_MS = 125_000; // acima dos 120 s do add-in (PROTOCOL.md § 2)

// ─── Tipos internos ───────────────────────────────────────────────────────────

interface RuntimeInfo {
  port:            number;
  pid:             number;
  protocolVersion: string;
  addinVersion:    string;
}

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject:  (reason: Error) => void;
  timer:   ReturnType<typeof setTimeout>;
}

// ─── RevitClient ──────────────────────────────────────────────────────────────

/**
 * Cliente WebSocket para o add-in do Revit.
 * Lê runtime.json, conecta, faz handshake e mantém keepalive (ARCHITECTURE § 6).
 *
 * Emite eventos:
 *   "status"           → StatusEventData
 *   "log"              → LogEventData        (TODO Fase 3)
 *   "document_changed" → DocumentChangedData (TODO Fase 3)
 *
 * NUNCA usa console.log — todos os logs vão via log.ts → process.stderr.
 */
export class RevitClient extends EventEmitter {
  private readonly _ws: WebSocket;
  private readonly _addinVersion: string;
  private readonly _pending = new Map<string, PendingRequest>();
  private _pingTimer?: ReturnType<typeof setInterval>;

  private constructor(ws: WebSocket, addinVersion: string) {
    super();
    this._ws           = ws;
    this._addinVersion = addinVersion;

    ws.on("message", (data) => this._onMessage(String(data)));
    ws.on("close",   ()     => { this._clearPing(); this.emit("close"); });
    ws.on("error",   (err)  => this.emit("error", err));
  }

  // ─── Fábrica estática ──────────────────────────────────────────────────────

  /**
   * Lê runtime.json, conecta ao add-in, valida handshake.
   * Loga caminho tentado, conteúdo lido e resultado da conexão.
   * Lança se qualquer etapa falhar.
   */
  static async connect(): Promise<RevitClient> {
    const runtime = readRuntimeJson(); // loga path + conteúdo internamente

    log.info(`Conectando a ws://127.0.0.1:${runtime.port} (addinVersion: ${runtime.addinVersion})…`);

    const ws = new WebSocket(`ws://127.0.0.1:${runtime.port}`);

    await new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(
        () => reject(new Error(`Timeout ao conectar ao add-in em 127.0.0.1:${runtime.port} (5 s).`)),
        5_000
      );
      ws.once("open",  () => { clearTimeout(timeout); resolve(); });
      ws.once("error", (err) => { clearTimeout(timeout); reject(err); });
    });

    log.debug(`WebSocket aberto em 127.0.0.1:${runtime.port}. Iniciando handshake…`);

    const client = new RevitClient(ws, runtime.addinVersion);
    await client._doHandshake();
    client._startPing();
    return client;
  }

  get addinVersion(): string { return this._addinVersion; }

  // ─── API pública ──────────────────────────────────────────────────────────

  async executeCode(params: ExecuteCodeParams): Promise<ExecuteCodeResult> {
    return this._request<ExecuteCodeResult>("execute_code", {
      code: params.code,
      mode: params.mode ?? null,
    });
  }

  // TODO Fase 3: getContext(), runTool(), listTools(), revertLast()

  // ─── Handshake ────────────────────────────────────────────────────────────

  private async _doHandshake(): Promise<void> {
    const params: HandshakeParams = {
      protocolVersion: PROTOCOL_VERSION,
      bridgeVersion:   "0.1.0", // TODO: ler do package.json em runtime
    };

    const result = await this._request<HandshakeResult>(
      "handshake",
      params as unknown as Record<string, unknown>
    );

    const ourMajor   = PROTOCOL_VERSION.split(".")[0];
    const theirMajor = result.protocolVersion.split(".")[0];

    if (ourMajor !== theirMajor) {
      this._ws.close();
      throw new Error(
        `PROTOCOL_MISMATCH: add-in usa protocolo ${result.protocolVersion}, ` +
        `ponte usa ${PROTOCOL_VERSION}. Atualize o ConectaRevit.`
      );
    }

    log.info(
      `Handshake OK — Revit ${result.revitVersion}, ` +
      `add-in ${result.addinVersion}, protocolo ${result.protocolVersion}.`
    );
  }

  // ─── Keepalive ────────────────────────────────────────────────────────────

  private _startPing(): void {
    this._pingTimer = setInterval(async () => {
      try {
        await this._request("ping", {});
      } catch (err) {
        // Ping failures isolados são esperados (Revit ocupado, execução longa).
        const msg = err instanceof Error ? err.message : String(err);
        log.debug(`Ping falhou: ${msg}`);
      }
    }, PING_INTERVAL_MS);
  }

  private _clearPing(): void {
    if (this._pingTimer) {
      clearInterval(this._pingTimer);
      this._pingTimer = undefined;
    }
  }

  // ─── Envio de request e correlação de response ───────────────────────────

  private _request<T>(method: string, params: Record<string, unknown>): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const id = randomUUID();

      const timer = setTimeout(() => {
        this._pending.delete(id);
        reject(new Error(
          `Timeout: o método '${method}' não respondeu em ${REQUEST_TIMEOUT_MS / 1000} s.`
        ));
      }, REQUEST_TIMEOUT_MS);

      this._pending.set(id, {
        resolve: resolve as (v: unknown) => void,
        reject,
        timer,
      });

      this._ws.send(JSON.stringify({ id, method, params }));
    });
  }

  // ─── Mensagens recebidas ──────────────────────────────────────────────────

  private _onMessage(raw: string): void {
    let msg: unknown;
    try { msg = JSON.parse(raw); } catch { return; }

    if (typeof msg !== "object" || msg === null) return;
    const obj = msg as Record<string, unknown>;

    // Evento: não tem id, tem "event" (PROTOCOL.md § 2.3)
    if ("event" in obj && !("id" in obj)) {
      this.emit(obj["event"] as string, obj["data"]);
      return;
    }

    // Response: tem id + ok (PROTOCOL.md § 2.2)
    if ("id" in obj && "ok" in obj) {
      const pending = this._pending.get(obj["id"] as string);
      if (!pending) return;

      clearTimeout(pending.timer);
      this._pending.delete(obj["id"] as string);

      if (obj["ok"] === true) {
        pending.resolve(obj["result"]);
      } else {
        const err = obj["error"] as { code: string; message: string } | undefined;
        pending.reject(new Error(err ? `${err.code}: ${err.message}` : "Erro desconhecido"));
      }
    }
  }
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

/**
 * Resolve o caminho do runtime.json com fallback robusto para quando o
 * Claude Desktop lança a ponte sem herdar a variável APPDATA.
 *
 * Ordem de tentativas:
 *   1. process.env.APPDATA  (Windows normal)
 *   2. os.homedir() + AppData\Roaming  (fallback quando APPDATA não está no env)
 */
function resolveRuntimeJsonPath(): string {
  let appData = process.env["APPDATA"];

  if (!appData) {
    const fallback = path.join(os.homedir(), "AppData", "Roaming");
    log.warn(
      `APPDATA não está definido no ambiente do processo. ` +
      `Usando fallback: ${fallback}`
    );
    appData = fallback;
  }

  const runtimePath = path.join(appData, "ConectaRevit", "runtime.json");
  log.debug(`runtime.json — caminho resolvido: ${runtimePath}`);
  return runtimePath;
}

function readRuntimeJson(): RuntimeInfo {
  const runtimePath = resolveRuntimeJsonPath();

  if (!fs.existsSync(runtimePath)) {
    log.warn(`runtime.json NÃO encontrado em: ${runtimePath}`);
    throw new Error(
      `runtime.json não encontrado em:\n  ${runtimePath}\n\n` +
      "Certifique-se de que o Revit está aberto e o botão \"Conectar\" " +
      "foi pressionado no painel ConectaRevit."
    );
  }

  const raw = fs.readFileSync(runtimePath, "utf8");
  log.debug(`runtime.json lido: ${raw.trim()}`);

  const info = JSON.parse(raw) as RuntimeInfo;
  log.info(
    `runtime.json — porta: ${info.port}, pid: ${info.pid}, ` +
    `addinVersion: ${info.addinVersion}, protocolVersion: ${info.protocolVersion}`
  );
  return info;
}
