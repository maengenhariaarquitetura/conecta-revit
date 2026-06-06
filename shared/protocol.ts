// Tipos de envelope do protocolo WebSocket (PROTOCOL.md § 2).
// Deve espelhar exatamente shared/Protocol.cs.
// Payloads específicos de cada método (handshake, execute_code, etc.): TODO Fase 2.

// § 2.1 — Request (ponte → add-in)
export interface Request {
  id: string;                              // UUID v4 gerado pela ponte
  method: string;
  params: Record<string, unknown>;
}

// § 2.2 — Response (add-in → ponte, correlacionada por id)
export interface Response {
  id: string;                              // mesmo UUID do Request correspondente
  ok: boolean;
}

export interface SuccessResponse<T = unknown> extends Response {
  ok: true;
  result: T;
}

export interface ErrorResponse extends Response {
  ok: false;
  error: ErrorInfo;
}

export interface ErrorInfo {
  code: string;
  message: string;                         // humano, pt-BR
  details?: string;
}

// § 2.3 — Event (add-in → ponte, sem id, sem resposta)
export type EventName = "log" | "status" | "document_changed";

export interface Event {
  event: EventName;
  data: Record<string, unknown>;           // TODO Fase 2: tipar payloads específicos (§ 4)
}
