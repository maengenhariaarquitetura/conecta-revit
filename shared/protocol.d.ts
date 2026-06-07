export declare const PROTOCOL_VERSION = "1.0";
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
    message: string;
    details?: string;
}
/** Evento add-in → ponte. Sem id, sem resposta esperada. */
export interface WsEvent<T = unknown> {
    event: EventName;
    data: T;
}
export type EventName = "log" | "status" | "document_changed";
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
export interface PingResult {
    pong: true;
    ts: number;
}
export interface ExecuteCodeParams {
    code: string;
    mode?: "safe" | "direct" | null;
}
export interface ExecuteCodeResult {
    returnValue: unknown;
    log: string[];
    transactionName: string | null;
    elementsCreated: number[];
}
export interface StatusEventData {
    connected: boolean;
    documentTitle: string | null;
    mode: "safe" | "direct";
}
