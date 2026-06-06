// Tipos de envelope do protocolo WebSocket (PROTOCOL.md § 2).
// Deve espelhar exatamente shared/protocol.ts.
// Payloads específicos de cada método (handshake, execute_code, etc.): TODO Fase 2.

using System.Text.Json.Serialization;

namespace ConectaRevit.Shared;

// § 2.1 — Request (ponte → add-in)
public record Request(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] object? Params
);

// § 2.2 — Response (add-in → ponte, correlacionada por id)
public record Response<T>(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("ok")]     bool Ok,
    [property: JsonPropertyName("result")] T? Result,
    [property: JsonPropertyName("error")]  ErrorInfo? Error
);

public record ErrorInfo(
    [property: JsonPropertyName("code")]    string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] string? Details
);

// § 2.3 — Event (add-in → ponte, sem id, sem resposta)
public record Event(
    [property: JsonPropertyName("event")] string EventType,
    [property: JsonPropertyName("data")]  object? Data
);
