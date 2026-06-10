using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.UI;
using ConectaRevit.Addin.Execution;
using ConectaRevit.Addin.Logging;
using ConectaRevit.Addin.Settings;
using ConectaRevit.Shared;

namespace ConectaRevit.Addin.Server;

// Servidor WebSocket em localhost para a ponte MCP (ARCHITECTURE § 5.2).
//
// Regras de design:
//   - Apenas loopback (127.0.0.1). PROTOCOL.md § 1.
//   - Porta 8765→8775: primeira livre vence.
//   - Ao subir grava runtime.json em %AppData%\ConectaRevit\.
//   - handshake e ping: tratados diretamente na thread do WS (não tocam API do Revit).
//   - execute_code: delegado ao ExecutionEngine via ExternalEvent + TCS.
//   - Sends serializados por _sendLock (WebSocket não suporta escrita concorrente).
//   - Handlers de mensagem: fire-and-forget — ping não fica preso atrás de execute_code.
internal sealed class WebSocketServer
{
    // ─── Dependências ────────────────────────────────────────────────────────

    private readonly ExecutionEngine _engine;
    private readonly SettingsManager _settings;

    // ─── Estado ──────────────────────────────────────────────────────────────

    private HttpListener?             _listener;
    private CancellationTokenSource?  _cts;
    private volatile WebSocket?       _currentWs;   // cliente ativo (um por vez)
    private readonly SemaphoreSlim    _sendLock = new(1, 1);
    private int _port;

    public bool IsRunning { get; private set; }
    /// <summary>Porta em uso pelo servidor (0 se não estiver rodando).</summary>
    public int  Port      => _port;

    // ─── Serialização JSON ───────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    // ─── Construtor ──────────────────────────────────────────────────────────

    internal WebSocketServer(ExecutionEngine engine, SettingsManager settings)
    {
        _engine   = engine;
        _settings = settings;
    }

    // ─── Start / Stop ────────────────────────────────────────────────────────

    public void Start()
    {
        if (IsRunning) return;

        AddinLog.Info("WebSocketServer.Start(): iniciando servidor WebSocket.");

        HttpListener? listener = null;
        int foundPort = -1;

        for (int port = 8765; port <= 8775; port++)
        {
            var candidate = new HttpListener();
            candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                candidate.Start();
                listener  = candidate;
                foundPort = port;
                break;
            }
            catch { candidate.Close(); }
        }

        if (listener == null)
            throw new InvalidOperationException(
                "Nenhuma porta disponível no intervalo 8765–8775. " +
                "Verifique se outra instância do ConectaRevit já está rodando.");

        _listener = listener;
        _port     = foundPort;
        _cts      = new CancellationTokenSource();
        IsRunning = true;

        AddinLog.Info(
            $"WebSocketServer.Start(): escutando na porta {foundPort}. " +
            $"Thread atual={Environment.CurrentManagedThreadId} " +
            $"(thread principal — AcceptLoop será movido para ThreadPool).");
        WriteRuntimeJson();

        // ── CORREÇÃO DE DEADLOCK ─────────────────────────────────────────────
        //
        // Problema original: AcceptLoopAsync era iniciado diretamente de Start(),
        // que é chamado de ConnectCommand.Execute() — a thread principal do Revit
        // (Thread 1). Sem Task.Run, todas as continuações async (HandleClientAsync,
        // HandleMessageAsync, HandleExecuteCodeAsync) ficavam capturadas no
        // SynchronizationContext da Thread 1.
        //
        // Quando HandleExecuteCodeAsync aguardava o TCS, a Thread 1 ficava
        // "ocupada" com a continuação pendente. O IExternalEventHandler.Execute()
        // só roda quando a Thread 1 está ociosa → deadlock.
        //
        // Solução: Task.Run() coloca AcceptLoopAsync em uma thread do ThreadPool,
        // onde SynchronizationContext.Current == null. Todas as continuações
        // subsequentes são despachadas para o ThreadPool, mantendo a Thread 1
        // completamente livre para o Revit despachar ExternalEvents.
        _ = Task.Run(async () =>
        {
            try
            {
                await AcceptLoopAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* encerramento normal via Stop() */ }
            catch (Exception ex)
            {
                var msg = ex.GetBaseException().Message;
                Debug.WriteLine($"[ConectaRevit] AcceptLoop encerrou com erro: {msg}");
                AddinLog.Error($"AcceptLoop encerrou com erro inesperado: {msg}");
            }
        });

        _ = BroadcastStatusAsync(connected: true);
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _ = BroadcastStatusAsync(connected: false);

        _cts?.Cancel();
        try { _listener?.Stop(); _listener?.Close(); } catch { }

        IsRunning = false;
        _listener = null;
    }

    // ─── Loop de accept ──────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        // Deve rodar em thread do ThreadPool (Thread != 1).
        // Se Thread == 1 aqui, o Task.Run em Start() não foi aplicado corretamente.
        AddinLog.Info(
            $"AcceptLoopAsync: iniciado. " +
            $"Thread={Environment.CurrentManagedThreadId} (esperado: ThreadPool, NÃO Thread 1). " +
            $"SC={System.Threading.SynchronizationContext.Current?.GetType().Name ?? "null (correto)"}.");

        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException)      { break; }
            catch (ObjectDisposedException)    { break; }

            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                continue;
            }

            if (!IPAddress.IsLoopback(ctx.Request.RemoteEndPoint.Address))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                continue;
            }

            _ = HandleClientAsync(ctx, ct);
        }

        AddinLog.Info("AcceptLoopAsync: encerrado.");
    }

    // ─── Sessão de um cliente ────────────────────────────────────────────────

    private async Task HandleClientAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        WebSocketContext wsCtx;
        try { wsCtx = await ctx.AcceptWebSocketAsync(null).ConfigureAwait(false); }
        catch { return; }

        var ws = wsCtx.WebSocket;
        _currentWs = ws;

        AddinLog.Info(
            $"HandleClientAsync: cliente WS conectado. " +
            $"Thread={Environment.CurrentManagedThreadId}.");

        var buffer = new byte[64 * 1024];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                                     .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct)
                                .ConfigureAwait(false);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());

                _ = HandleMessageAsync(ws, json, ct).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        var msg = t.Exception?.GetBaseException().Message;
                        Debug.WriteLine($"[ConectaRevit] Erro ao processar mensagem: {msg}");
                        AddinLog.Error($"HandleClientAsync: erro no handler de mensagem: {msg}");
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            AddinLog.Warn($"HandleClientAsync: WebSocketException (cliente desconectou?): {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_currentWs, ws))
                _currentWs = null;
            AddinLog.Info("HandleClientAsync: sessão encerrada.");
        }
    }

    // ─── Roteamento de mensagens ─────────────────────────────────────────────

    private async Task HandleMessageAsync(WebSocket ws, string json, CancellationToken ct)
    {
        string id     = "";
        string method = "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id",     out var idEl)     ||
                !root.TryGetProperty("method", out var methodEl))
                return;

            id     = idEl.GetString()     ?? "";
            method = methodEl.GetString() ?? "";

            AddinLog.Info(
                $"HandleMessageAsync: method='{method}' id='{id}' " +
                $"Thread={Environment.CurrentManagedThreadId} " +
                $"(esperado: ThreadPool, NÃO Thread 1).");

            JsonElement paramsEl = default;
            root.TryGetProperty("params", out paramsEl);

            switch (method)
            {
                case "handshake":
                    await HandleHandshakeAsync(ws, id, paramsEl, ct).ConfigureAwait(false);
                    break;
                case "ping":
                    await HandlePingAsync(ws, id, ct).ConfigureAwait(false);
                    break;
                case "execute_code":
                    await HandleExecuteCodeAsync(ws, id, paramsEl, ct).ConfigureAwait(false);
                    break;
                case "get_context":
                    await HandleGetContextAsync(ws, id, ct).ConfigureAwait(false);
                    break;
                case "revert_last":
                    await HandleRevertLastAsync(ws, id, ct).ConfigureAwait(false);
                    break;
                default:
                    await SendErrorAsync(ws, id,
                        "METHOD_NOT_FOUND", $"Método desconhecido: {method}", null, ct)
                        .ConfigureAwait(false);
                    break;
            }
        }
        catch (JsonException)
        {
            await SendErrorAsync(ws, id, "INVALID_ARGS", "JSON inválido", null, ct);
        }
    }

    // ─── Handlers de cada método ─────────────────────────────────────────────

    // handshake (PROTOCOL.md § 3.1)
    private async Task HandleHandshakeAsync(WebSocket ws, string id, JsonElement paramsEl, CancellationToken ct)
    {
        HandshakeParams? p;
        try { p = paramsEl.Deserialize<HandshakeParams>(JsonOpts); }
        catch { p = null; }

        if (p == null)
        {
            await SendErrorAsync(ws, id, "INVALID_ARGS", "Params do handshake inválidos", null, ct);
            return;
        }

        var bridgeMajor = p.ProtocolVersion.Split('.')[0];
        var ourMajor    = Application.ProtocolVersion.Split('.')[0];

        if (bridgeMajor != ourMajor)
        {
            await SendErrorAsync(ws, id, "PROTOCOL_MISMATCH",
                $"Versão de protocolo incompatível: add-in usa {Application.ProtocolVersion}, " +
                $"ponte usa {p.ProtocolVersion}. Atualize o ConectaRevit.",
                null, ct);
            return;
        }

        // Lê o documento ativo (pode ser null se nenhum projeto aberto).
        var documentTitle = Application.UiApplication?.ActiveUIDocument?.Document?.Title;

        var result = new HandshakeResult(
            ProtocolVersion: Application.ProtocolVersion,
            AddinVersion:    Application.AddinVersion,
            RevitVersion:    Application.RevitVersion,
            DocumentTitle:   documentTitle,
            Mode:            _settings.Mode
        );

        await SendSuccessAsync(ws, id, result, ct);
        await EmitEventAsync(ws, "status",
            new StatusEventData(true, documentTitle, _settings.Mode), ct);
    }

    // ping (PROTOCOL.md § 3.2)
    private async Task HandlePingAsync(WebSocket ws, string id, CancellationToken ct)
    {
        var result = new PingResult(true, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await SendSuccessAsync(ws, id, result, ct);
    }

    // execute_code (PROTOCOL.md § 3.4)
    private async Task HandleExecuteCodeAsync(WebSocket ws, string id, JsonElement paramsEl, CancellationToken ct)
    {
        ExecuteCodeParams? p;
        try { p = paramsEl.Deserialize<ExecuteCodeParams>(JsonOpts); }
        catch { p = null; }

        if (p == null || string.IsNullOrWhiteSpace(p.Code))
        {
            await SendErrorAsync(ws, id, "INVALID_ARGS",
                "O campo 'code' é obrigatório em execute_code.", null, ct);
            return;
        }

        AddinLog.Info($"HandleExecuteCodeAsync: id='{id}' — aguardando TCS do ExecutionEngine.");

        // Timeout de 120 s no lado do add-in (ARCHITECTURE § 5.2).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        try
        {
            var result = await _engine.EnqueueAsync(p, cts.Token).ConfigureAwait(false);
            AddinLog.Info($"HandleExecuteCodeAsync: id='{id}' — TCS resolvido com sucesso. Enviando resposta.");
            await SendSuccessAsync(ws, id, result, ct).ConfigureAwait(false);
        }
        // ── Erros específicos do protocolo ──────────────────────────────────
        catch (CompilationException ex)
        {
            AddinLog.Warn($"HandleExecuteCodeAsync: id='{id}' — COMPILATION_ERROR.");
            await SendErrorAsync(ws, id, "COMPILATION_ERROR",
                "O código C# não compilou. Verifique os diagnósticos.",
                ex.Diagnostics, ct);
        }
        catch (NoDocumentException ex)
        {
            AddinLog.Warn($"HandleExecuteCodeAsync: id='{id}' — NO_DOCUMENT: {ex.Message}");
            await SendErrorAsync(ws, id, "NO_DOCUMENT", ex.Message, null, ct);
        }
        catch (TransactionFailedException ex)
        {
            AddinLog.Error($"HandleExecuteCodeAsync: id='{id}' — TRANSACTION_FAILED: {ex.Details}");
            await SendErrorAsync(ws, id, "TRANSACTION_FAILED",
                "Falha na transação do harness.", ex.Details, ct);
        }
        // ── Controle de fluxo ───────────────────────────────────────────────
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            AddinLog.Warn($"HandleExecuteCodeAsync: id='{id}' — TIMEOUT após 120 s.");
            await SendErrorAsync(ws, id, "TIMEOUT",
                "A execução excedeu o limite de 120 s.", null, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "BUSY")
        {
            AddinLog.Warn($"HandleExecuteCodeAsync: id='{id}' — BUSY.");
            await SendErrorAsync(ws, id, "BUSY",
                "Fila cheia (>10 requisições pendentes). Aguarde.", null, ct);
        }
        // ── Timeout no add-in (60 s) — TCS resolvido pelo background callback ──
        catch (TimeoutException ex)
        {
            AddinLog.Error($"HandleExecuteCodeAsync: id='{id}' — TIMEOUT no add-in: {ex.Message}");
            await SendErrorAsync(ws, id, "TIMEOUT",
                "A execução excedeu 60s no add-in. O script pode estar em loop ou travado.",
                null, ct);
        }
        // ── Runtime error (exceção lançada pelo script do usuário) ──────────
        catch (Exception ex)
        {
            AddinLog.Error($"HandleExecuteCodeAsync: id='{id}' — RUNTIME_ERROR: {ex.GetType().Name}: {ex.Message}");

            // Detecta tentativa de abrir Transaction aninhada em Modo Seguro.
            // O harness já abre uma Transaction antes do script; se o script
            // tentar abrir outra, o Revit lança InvalidOperationException com
            // "transaction" na mensagem. Instruímos o modelo a corrigir.
            var isNestedTx = ex is InvalidOperationException
                && ex.Message.IndexOf("transaction", StringComparison.OrdinalIgnoreCase) >= 0;

            var primaryMsg = isNestedTx
                ? "Em Modo Seguro não abra Transaction; escreva a modificação direto " +
                  "(ex.: Wall.Create(Doc, ...)). O harness já gerencia a Transaction automaticamente."
                : $"{ex.GetType().Name}: {ex.Message}";

            var summary = FormatRuntimeError(ex);
            await SendErrorAsync(ws, id, "RUNTIME_ERROR", primaryMsg, summary, ct);
        }
    }

    // get_context (PROTOCOL.md § 3.3)
    private async Task HandleGetContextAsync(WebSocket ws, string id, CancellationToken ct)
    {
        AddinLog.Info($"HandleGetContextAsync: id='{id}'.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var result = await _engine.GetContextAsync(cts.Token).ConfigureAwait(false);
            AddinLog.Info($"HandleGetContextAsync: id='{id}' — OK. doc='{result.DocumentTitle}'.");
            await SendSuccessAsync(ws, id, result, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await SendErrorAsync(ws, id, "TIMEOUT",
                "get_context excedeu 30s no add-in.", null, ct);
        }
        catch (Exception ex)
        {
            AddinLog.Error($"HandleGetContextAsync: id='{id}' — RUNTIME_ERROR: {ex.Message}");
            await SendErrorAsync(ws, id, "RUNTIME_ERROR",
                $"{ex.GetType().Name}: {ex.Message}", FormatRuntimeError(ex), ct);
        }
    }

    // revert_last (PROTOCOL.md § 3.7)
    private async Task HandleRevertLastAsync(WebSocket ws, string id, CancellationToken ct)
    {
        AddinLog.Info($"HandleRevertLastAsync: id='{id}'.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var result = await _engine.RevertLastAsync(cts.Token).ConfigureAwait(false);
            AddinLog.Info(
                $"HandleRevertLastAsync: id='{id}' — OK. " +
                $"reverted={result.Reverted}, tx='{result.TransactionName}'.");
            await SendSuccessAsync(ws, id, result, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await SendErrorAsync(ws, id, "TIMEOUT",
                "revert_last excedeu 30s no add-in.", null, ct);
        }
        catch (Exception ex)
        {
            AddinLog.Error($"HandleRevertLastAsync: id='{id}' — RUNTIME_ERROR: {ex.Message}");
            await SendErrorAsync(ws, id, "RUNTIME_ERROR",
                $"{ex.GetType().Name}: {ex.Message}", FormatRuntimeError(ex), ct);
        }
    }

    // ─── Helpers de envio ────────────────────────────────────────────────────

    private async Task SendSuccessAsync<T>(WebSocket ws, string id, T result, CancellationToken ct)
    {
        var response = new SuccessResponse<T>(id, true, result);
        var json = JsonSerializer.Serialize(response, JsonOpts);
        AddinLog.Info($"SendSuccessAsync: id='{id}' — enviando {json.Length} bytes.");
        await SendTextAsync(ws, json, ct);
    }

    private async Task SendErrorAsync(WebSocket ws, string id,
        string code, string message, string? details, CancellationToken ct)
    {
        var response = new ErrorResponse(id, false, new ErrorInfo(code, message, details));
        var json = JsonSerializer.Serialize(response, JsonOpts);
        AddinLog.Warn($"SendErrorAsync: id='{id}' code='{code}' — enviando resposta de erro.");
        await SendTextAsync(ws, json, ct);
    }

    private async Task EmitEventAsync<T>(WebSocket ws, string eventName, T data, CancellationToken ct)
    {
        var evt = new WsEvent<T>(eventName, data);
        await SendTextAsync(ws, JsonSerializer.Serialize(evt, JsonOpts), ct);
    }

    private async Task BroadcastStatusAsync(bool connected)
    {
        var ws = _currentWs;
        if (ws?.State != WebSocketState.Open) return;
        try
        {
            var docTitle = Application.UiApplication?.ActiveUIDocument?.Document?.Title;
            await EmitEventAsync(ws, "status",
                new StatusEventData(connected, docTitle, _settings.Mode),
                CancellationToken.None);
        }
        catch { /* cliente desconectou */ }
    }

    // Serializa writes no WebSocket (não suporta concorrência nativa).
    private async Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct).ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    // ─── runtime.json ────────────────────────────────────────────────────────

    private void WriteRuntimeJson()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConectaRevit");
        Directory.CreateDirectory(dir);

        var data = new
        {
            port            = _port,
            pid             = Environment.ProcessId,
            protocolVersion = Application.ProtocolVersion,
            addinVersion    = Application.AddinVersion
        };

        File.WriteAllText(
            Path.Combine(dir, "runtime.json"),
            JsonSerializer.Serialize(data, JsonOpts));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Formata um erro de runtime para o campo details da resposta.
    /// Inclui tipo, mensagem e as primeiras 10 linhas do stack trace.
    /// </summary>
    private static string FormatRuntimeError(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Tipo: {ex.GetType().FullName}");
        sb.AppendLine($"Mensagem: {ex.Message}");

        if (ex.StackTrace != null)
        {
            var lines = ex.StackTrace.Split('\n').Take(10);
            sb.AppendLine("Stack trace (primeiras 10 linhas):");
            foreach (var line in lines)
                sb.AppendLine(line.TrimEnd());
        }

        if (ex.InnerException != null)
            sb.AppendLine($"Causa: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");

        return sb.ToString().TrimEnd();
    }
}
