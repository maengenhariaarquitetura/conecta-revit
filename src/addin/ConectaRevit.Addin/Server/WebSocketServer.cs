namespace ConectaRevit.Addin.Server;

// Hospeda o servidor WebSocket em localhost para a ponte MCP (ARCHITECTURE § 5.2).
// TODO Fase 3: implementar com System.Net.HttpListener + System.Net.WebSockets (sem libs externas).
//
// Contrato:
//   - Porta default 8765; tenta 8766..8775 se a porta estiver ocupada.
//   - Ao subir: grava %AppData%\ConectaRevit\runtime.json = { port, pid, protocolVersion, addinVersion }.
//   - Aceita conexões apenas de 127.0.0.1 / ::1 (PROTOCOL.md § 1).
//   - Processa requisições serialmente (fila); timeout 120s; máx. 10 na fila → devolve BUSY.
//   - NUNCA chama a API do Revit diretamente — sempre via ExecutionEngine (ExternalEvent).
//   - Roda em thread de background.
internal sealed class WebSocketServer
{
    // TODO Fase 3: implementar Start(), Stop(), loop de recepção e despacho para ExecutionEngine.
    public void Start() { }
    public void Stop() { }
}
