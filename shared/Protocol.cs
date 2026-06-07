// Tipos de envelope e de métodos do protocolo WebSocket (PROTOCOL.md §§ 2–3).
// Espelha exatamente shared/protocol.ts — mesmos campos, mesmos nomes, mesma ordem.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConectaRevit.Shared;

// ─── Envelopes (§ 2) ─────────────────────────────────────────────────────────

/// <summary>Mensagem ponte → add-in. id = UUID v4 gerado pela ponte.</summary>
public record Request(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params
);

/// <summary>Resposta de sucesso add-in → ponte, correlacionada pelo id.</summary>
public record SuccessResponse<T>(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("ok")]     bool Ok,
    [property: JsonPropertyName("result")] T Result
);

/// <summary>Resposta de erro add-in → ponte.</summary>
public record ErrorResponse(
    [property: JsonPropertyName("id")]    string Id,
    [property: JsonPropertyName("ok")]    bool Ok,
    [property: JsonPropertyName("error")] ErrorInfo Error
);

public record ErrorInfo(
    [property: JsonPropertyName("code")]    string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] string? Details
);

/// <summary>Evento add-in → ponte. Sem id, sem resposta esperada.</summary>
public record WsEvent<T>(
    [property: JsonPropertyName("event")] string EventName,
    [property: JsonPropertyName("data")]  T Data
);

// ─── Método: handshake (§ 3.1) ───────────────────────────────────────────────

public record HandshakeParams(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("bridgeVersion")]   string BridgeVersion
);

public record HandshakeResult(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("addinVersion")]    string AddinVersion,
    [property: JsonPropertyName("revitVersion")]    string RevitVersion,
    [property: JsonPropertyName("documentTitle")]   string? DocumentTitle,
    [property: JsonPropertyName("mode")]            string Mode
);

// ─── Método: ping (§ 3.2) ────────────────────────────────────────────────────

public record PingResult(
    [property: JsonPropertyName("pong")] bool Pong,
    [property: JsonPropertyName("ts")]   long Ts
);

// ─── Método: execute_code (§ 3.4) ────────────────────────────────────────────

public record ExecuteCodeParams(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("mode")] string? Mode
);

public record ExecuteCodeResult(
    [property: JsonPropertyName("returnValue")]      object? ReturnValue,
    [property: JsonPropertyName("log")]              List<string> Log,
    [property: JsonPropertyName("transactionName")]  string? TransactionName,
    [property: JsonPropertyName("elementsCreated")]  List<long> ElementsCreated,
    [property: JsonPropertyName("elementsModified")] List<long> ElementsModified,
    [property: JsonPropertyName("elementsDeleted")]  List<long> ElementsDeleted
);

// ─── Método: get_context (§ 3.3) ─────────────────────────────────────────────

/// <summary>Item de seleção retornado por get_context.</summary>
public record SelectionItem(
    [property: JsonPropertyName("id")]       long   Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("name")]     string Name
);

public record GetContextResult(
    [property: JsonPropertyName("documentTitle")]    string?          DocumentTitle,
    [property: JsonPropertyName("isFamilyDocument")] bool             IsFamilyDocument,
    [property: JsonPropertyName("activeViewId")]     long?            ActiveViewId,
    [property: JsonPropertyName("activeViewName")]   string?          ActiveViewName,
    [property: JsonPropertyName("activeViewType")]   string?          ActiveViewType,
    [property: JsonPropertyName("unitSystem")]       string           UnitSystem,
    [property: JsonPropertyName("selection")]        List<SelectionItem> Selection
);

// ─── Método: revert_last (§ 3.7) ─────────────────────────────────────────────

public record RevertLastResult(
    [property: JsonPropertyName("reverted")]         bool    Reverted,
    [property: JsonPropertyName("transactionName")]  string? TransactionName
);

// ─── Métodos: run_tool, list_tools ───────────────────────────────────────────
// TODO Fase 3D: adicionar params/result de run_tool, list_tools.

// ─── Dados de eventos (§ 4) ──────────────────────────────────────────────────

public record StatusEventData(
    [property: JsonPropertyName("connected")]     bool Connected,
    [property: JsonPropertyName("documentTitle")] string? DocumentTitle,
    [property: JsonPropertyName("mode")]          string Mode
);

// TODO Fase 3: adicionar LogEventData, DocumentChangedEventData.
