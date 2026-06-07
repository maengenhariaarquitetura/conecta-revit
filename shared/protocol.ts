// Tipos de envelope e de métodos do protocolo WebSocket (PROTOCOL.md §§ 2–3).
// Espelha exatamente shared/Protocol.cs — mesmos campos, mesmos nomes, mesma ordem.

export const PROTOCOL_VERSION = "1.0";

// ─── Envelopes (§ 2) ─────────────────────────────────────────────────────────

/** Mensagem ponte → add-in. id = UUID v4 gerado pela ponte. */
export interface Request {
  id: string;
  method: string;
  params: Record<string, unknown>;
}

/** Resposta de sucesso add-in → ponte, correlacionada pelo id. */
export interface SuccessResponse<T = unknown> {
  id: string;
  ok: true;
  result: T;
}

/** Resposta de erro add-in → ponte. */
export interface ErrorResponse {
  id: string;
  ok: false;
  error: ErrorInfo;
}

export type Response<T = unknown> = SuccessResponse<T> | ErrorResponse;

export interface ErrorInfo {
  code: string;
  message: string; // humano, pt-BR
  details?: string;
}

/** Evento add-in → ponte. Sem id, sem resposta esperada. */
export interface WsEvent<T = unknown> {
  event: EventName;
  data: T;
}

export type EventName = "log" | "status" | "document_changed";

// ─── Método: handshake (§ 3.1) ───────────────────────────────────────────────

export interface HandshakeParams {
  protocolVersion: string;
  bridgeVersion: string;
}

export interface HandshakeResult {
  protocolVersion: string;
  addinVersion: string;
  revitVersion: string;
  documentTitle: string | null;
  mode: "safe" | "direct";
}

// ─── Método: ping (§ 3.2) ────────────────────────────────────────────────────

export interface PingResult {
  pong: true;
  ts: number; // epoch ms
}

// ─── Método: execute_code (§ 3.4) ────────────────────────────────────────────

export interface ExecuteCodeParams {
  code: string;
  mode?: "safe" | "direct" | null;
}

export interface ExecuteCodeResult {
  returnValue: unknown;
  log: string[];
  transactionName: string | null;
  elementsCreated:  number[];
  elementsModified: number[];
  elementsDeleted:  number[];
}

// ─── Métodos: get_context, run_tool, list_tools, revert_last ─────────────────
// TODO Fase 3: adicionar params/result de get_context, run_tool, list_tools, revert_last.

// ─── Dados de eventos (§ 4) ──────────────────────────────────────────────────

export interface StatusEventData {
  connected: boolean;
  documentTitle: string | null;
  mode: "safe" | "direct";
}

// TODO Fase 3: adicionar LogEventData, DocumentChangedEventData.
