// TODO Fase 2: implementar cliente WebSocket para o add-in do Revit.
//
// Responsabilidade (PROTOCOL.md § 1–2):
//   - Conectar ao add-in na porta lida de %AppData%\ConectaRevit\runtime.json.
//   - Apenas aceitar conexões de 127.0.0.1/::1 (add-in só escuta loopback).
//   - Enviar Requests com UUID v4 gerado aqui; correlacionar Responses pelo mesmo id.
//   - Timeout de 125s por requisição pendente (PROTOCOL.md § 2).
//   - Escutar Events sem id (log, status, document_changed) e emitir como EventEmitter.
//   - Reconectar automaticamente em queda de conexão.
//   - Manter keepalive: enviar ping a cada 30s (PROTOCOL.md § 3.2).
