using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConectaRevit.Addin.Execution;
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

    private HttpListener?         _listener;
    private CancellationTokenSource? _cts;
    private volatile WebSocket?   _currentWs;   // cliente ativo (um por vez, Fase 2)
    private readonly SemaphoreSlim _sendLock = new(1, 1); // serializa ws.SendAsync
    private int _port;

    public bool IsRunning { get; private set; }

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

    /// <summary>
    /// Liga o servidor. HttpListener.Start() é síncrono; o loop de accept sobe em background.
    /// Lança exceção se nenhuma porta disponível — o ConnectCommand captura e exibe TaskDialog.
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        // Descoberta de porta: 8765 → 8775 (ARCHITECTURE § 5.2).
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
            catch
            {
                candidate.Close();
            }
        }

        if (listener == null)
            throw new InvalidOperationException(
                "Nenhuma porta disponível no intervalo 8765–8775. " +
                "Verifique se outra instância do ConectaRevit já está rodando.");

        _listener = listener;
        _port     = foundPort;
        _cts      = new CancellationTokenSource();
        IsRunning = true;

        WriteRuntimeJson();

        // Loop de accept em background — não bloqueia a thread principal do Revit.
        _ = AcceptLoopAsync(_cts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.WriteLine($"[ConectaRevit] AcceptLoop encerrou com erro: {t.Exception?.GetBaseException().Message}");
        });

        // Notifica a ponte (se já conectada) que o servidor subiu.
        _ = BroadcastStatusAsync(connected: true);
    }

    /// <summary>Para o servidor e notifica o cliente conectado.</summary>
    public void Stop()
    {
        if (!IsRunning) return;

        // Notifica o cliente antes de encerrar.
        _ = BroadcastStatusAsync(connected: false);

        _cts?.Cancel();
        try { _listener?.Stop(); _listener?.Close(); } catch { }

        IsRunning = false;
        _listener = null;
    }

    // ─── Loop de accept ──────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
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

            // Apenas loopback (PROTOCOL.md § 1).
            if (!IPAddress.IsLoopback(ctx.Request.RemoteEndPoint.Address))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                continue;
            }

            // Fire-and-forget por cliente — um cliente por vez nesta fase.
            _ = HandleClientAsync(ctx, ct);
        }
    }

    // ─── Sessão de um cliente ────────────────────────────────────────────────

    private async Task HandleClientAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        WebSocketContext wsCtx;
        try { wsCtx = await ctx.AcceptWebSocketAsync(null); }
        catch { return; }

        var ws = wsCtx.WebSocket;
        _currentWs = ws;

        var buffer = new byte[64 * 1024];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // Lê mensagem completa (pode ser multi-frame).
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());

                // Fire-and-forget: ping não fica preso atrás de execute_code em fila.
                _ = HandleMessageAsync(ws, json, ct).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Debug.WriteLine($"[ConectaRevit] Erro ao processar mensagem: {t.Exception?.GetBaseException().Message}");
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (ReferenceEquals(_currentWs, ws))
                _currentWs = null;
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

            if (!root.TryGetProperty("id", out var idEl) ||
                !root.TryGetProperty("method", out var methodEl))
                return; // mensagem malformada — ignora

            id     = idEl.GetString()     ?? "";
            method = methodEl.GetString() ?? "";

            JsonElement paramsEl = default;
            root.TryGetProperty("params", out paramsEl);

            switch (method)
            {
                case "handshake":
                    await HandleHandshakeAsync(ws, id, paramsEl, ct);
                    break;
                case "ping":
                    await HandlePingAsync(ws, id, ct);
                    break;
                case "execute_code":
                    await HandleExecuteCodeAsync(ws, id, paramsEl, ct);
                    break;
                default:
                    await SendErrorAsync(ws, id,
                        "METHOD_NOT_FOUND", $"Método desconhecido: {method}", null, ct);
                    break;
            }
        }
        catch (JsonException)
        {
            await SendErrorAsync(ws, id, "INVALID_ARGS", "JSON inválido", null, ct);
        }
    }

    // ─── Handlers de cada método ─────────────────────────────────────────────

    // handshake: valida MAJOR do protocolo, responde com info do add-in (PROTOCOL.md § 3.1).
    // RevitVersion lida do cache em Application.RevitVersion — não exige chamada de API.
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

        var result = new HandshakeResult(
            ProtocolVersion: Application.ProtocolVersion,
            AddinVersion:    Application.AddinVersion,
            RevitVersion:    Application.RevitVersion,   // string em cache — seguro em bg thread
            DocumentTitle:   null,                       // TODO Fase 3: ler UIDocument
            Mode:            _settings.Mode
        );

        await SendSuccessAsync(ws, id, result, ct);

        // Evento status logo após handshake (PROTOCOL.md § 4.2).
        await EmitEventAsync(ws, "status",
            new StatusEventData(true, null, _settings.Mode), ct);
    }

    // ping: keepalive da ponte a cada 30 s (PROTOCOL.md § 3.2).
    private async Task HandlePingAsync(WebSocket ws, string id, CancellationToken ct)
    {
        var result = new PingResult(true, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await SendSuccessAsync(ws, id, result, ct);
    }

    // execute_code: delega ao ExecutionEngine via ExternalEvent (PROTOCOL.md § 3.4).
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

        // Timeout de 120 s no lado do add-in (ARCHITECTURE § 5.2).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        try
        {
            var result = await _engine.EnqueueAsync(p, cts.Token);
            await SendSuccessAsync(ws, id, result, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout do job (não cancelamento da sessão WS).
            await SendErrorAsync(ws, id, "TIMEOUT",
                "A execução excedeu o limite de 120 s.", null, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "BUSY")
        {
            await SendErrorAsync(ws, id, "BUSY",
                "Fila cheia (>10 requisições pendentes). Aguarde.", null, ct);
        }
        catch (Exception ex)
        {
            await SendErrorAsync(ws, id, "RUNTIME_ERROR",
                "Erro inesperado na execução.", ex.Message, ct);
        }
    }

    // ─── Helpers de envio ────────────────────────────────────────────────────

    private async Task SendSuccessAsync<T>(WebSocket ws, string id, T result, CancellationToken ct)
    {
        var response = new SuccessResponse<T>(id, true, result);
        await SendTextAsync(ws, JsonSerializer.Serialize(response, JsonOpts), ct);
    }

    private async Task SendErrorAsync(WebSocket ws, string id,
        string code, string message, string? details, CancellationToken ct)
    {
        var response = new ErrorResponse(id, false, new ErrorInfo(code, message, details));
        await SendTextAsync(ws, JsonSerializer.Serialize(response, JsonOpts), ct);
    }

    private async Task EmitEventAsync<T>(WebSocket ws, string eventName, T data, CancellationToken ct)
    {
        var evt = new WsEvent<T>(eventName, data);
        await SendTextAsync(ws, JsonSerializer.Serialize(evt, JsonOpts), ct);
    }

    // Envia para o cliente atual (usado pelo Start/Stop para emitir status).
    private async Task BroadcastStatusAsync(bool connected)
    {
        var ws = _currentWs;
        if (ws?.State != WebSocketState.Open) return;
        try
        {
            await EmitEventAsync(ws, "status",
                new StatusEventData(connected, null, _settings.Mode),
                CancellationToken.None);
        }
        catch { /* cliente desconectou — ignora */ }
    }

    // Serializa writes no WebSocket (não suporta concorrência nativa).
    private async Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        finally
        {
            _sendLock.Release();
        }
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
}
