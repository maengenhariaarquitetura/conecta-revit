using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConectaRevit.Addin.Logging;
using Microsoft.Win32;

namespace ConectaRevit.Addin.Licensing;

// ── DTOs internos ──────────────────────────────────────────────────────────────

/// <summary>Resposta deserializada da Edge Function validate-license.</summary>
internal sealed record EdgeFunctionResponse
{
    [JsonPropertyName("valid")]      public bool    Valid      { get; init; }
    [JsonPropertyName("reason")]     public string? Reason     { get; init; }
    [JsonPropertyName("plan")]       public string? Plan       { get; init; }
    [JsonPropertyName("expires_at")] public string? ExpiresAt  { get; init; }
    [JsonPropertyName("signature")]  public string? Signature  { get; init; }
}

/// <summary>Resultado devolvido ao chamador (ConnectCommand / SettingsCommand).</summary>
internal sealed record LicenseValidationResult(bool Valid, string? Reason)
{
    public string?   Plan      { get; init; }
    public DateTime? ExpiresAt { get; init; }
    /// <summary>True quando a validação veio do cache offline (grace period).</summary>
    public bool      IsGrace   { get; init; }
}

// ── Status da licença ──────────────────────────────────────────────────────────

internal enum LicenseStatus
{
    /// <summary>Ainda não validada nesta sessão.</summary>
    Unknown,
    /// <summary>Válida, confirmada online.</summary>
    Valid,
    /// <summary>Inválida (chave errada, suspensa, mismatch).</summary>
    Invalid,
    /// <summary>Válida via cache, dentro do grace period offline.</summary>
    GraceOffline,
    /// <summary>Expirada (expires_at no passado).</summary>
    Expired,
    /// <summary>Chave não configurada nas Settings.</summary>
    NoKey,
}

// ── LicenseManager ─────────────────────────────────────────────────────────────

/// <summary>
/// Valida e cacheia a licença do ConectaRevit (ARCHITECTURE § 5.7).
///
/// Fluxo:
///   1. Tenta validação online → Edge Function validate-license.
///   2. Se offline ou erro de rede → usa cache local (grace period de 7 dias).
///   3. Cache expirado ou ausente + offline → bloqueia.
///
/// Thread-safety:
///   Destinado a ser chamado na Thread 1 (IExternalCommand.Execute).
///   ValidateAsync pode ser aguardado com .GetAwaiter().GetResult().
/// </summary>
internal sealed class LicenseManager
{
    // ── Estado público ────────────────────────────────────────────────────────

    public LicenseStatus Status     { get; private set; } = LicenseStatus.Unknown;
    public string         Plan      { get; private set; } = "";
    public DateTime?      ExpiresAt { get; private set; }

    /// <summary>
    /// True se a conexão pode prosseguir (válida online ou em grace offline).
    /// </summary>
    public bool IsConnectAllowed =>
        Status is LicenseStatus.Valid or LicenseStatus.GraceOffline;

    // ── Machine ID (lazy, determinístico) ─────────────────────────────────────

    private string? _machineId;

    /// <summary>
    /// Hash SHA-256 estável de (MachineGuid do registro + MachineName).
    /// Primeiros 32 hex chars (128 bits de entropia suficiente).
    /// </summary>
    public string GetMachineId()
    {
        if (_machineId != null) return _machineId;
        try
        {
            var guid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                "MachineGuid", null) as string ?? "";
            var combined = $"{guid}|{Environment.MachineName}";
            using var sha  = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
            _machineId = Convert.ToHexString(hash).ToLower()[..32];
        }
        catch
        {
            using var sha  = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName));
            _machineId = Convert.ToHexString(hash).ToLower()[..32];
        }
        return _machineId;
    }

    // ── Validação principal ───────────────────────────────────────────────────

    /// <summary>
    /// Valida a licença. Tenta online primeiro; em caso de falha de rede usa
    /// o cache local com grace period.
    /// Não lança exceções — erros de rede/config são tratados internamente.
    /// </summary>
    public async Task<LicenseValidationResult> ValidateAsync(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            Status = LicenseStatus.NoKey;
            return new(false, "no_key");
        }

        var machineId = GetMachineId();

        // ── 1. Tentativa online ───────────────────────────────────────────────
        try
        {
            var resp = await CallEdgeFunctionAsync(licenseKey, machineId).ConfigureAwait(false);

            if (resp != null)
            {
                if (resp.Valid)
                {
                    // Verificar assinatura HMAC (anti-proxy forjado)
                    if (!VerifySignature(licenseKey, machineId, resp.ExpiresAt ?? "", resp.Signature ?? ""))
                    {
                        AddinLog.Warn("validate-license: assinatura HMAC da resposta inválida — possível adulteração.");
                        Status = LicenseStatus.Invalid;
                        LicenseCache.Delete();
                        return new(false, "signature_mismatch");
                    }

                    // Verificar expiração
                    DateTime? expiry = null;
                    if (!string.IsNullOrEmpty(resp.ExpiresAt) &&
                        DateTime.TryParse(resp.ExpiresAt, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var parsedExp))
                    {
                        expiry = parsedExp.ToUniversalTime();
                        if (expiry < DateTime.UtcNow)
                        {
                            Status = LicenseStatus.Expired;
                            LicenseCache.Delete();
                            AddinLog.Warn($"Licença expirada em {expiry:o}.");
                            return new(false, "expired");
                        }
                    }

                    // Atualizar cache
                    var cacheData = new LicenseCacheData
                    {
                        Valid       = true,
                        Plan        = resp.Plan ?? "standard",
                        ExpiresAt   = resp.ExpiresAt ?? "",
                        ValidatedAt = DateTime.UtcNow.ToString("o"),
                        MachineId   = machineId,
                        Signature   = resp.Signature ?? "",
                    };
                    LicenseCache.Write(cacheData);

                    Status    = LicenseStatus.Valid;
                    Plan      = cacheData.Plan;
                    ExpiresAt = expiry;
                    AddinLog.Info($"Licença validada online. Plano={Plan}, Expira={resp.ExpiresAt ?? "nunca"}.");
                    return new(true, null) { Plan = Plan, ExpiresAt = ExpiresAt };
                }
                else
                {
                    // Servidor confirma inválida → limpa cache e bloqueia
                    LicenseCache.Delete();
                    Status = LicenseStatus.Invalid;
                    AddinLog.Warn($"Licença inválida (online): reason={resp.Reason}.");
                    return new(false, resp.Reason);
                }
            }
        }
        catch (Exception ex)
        {
            // Timeout, sem rede, Supabase pausado, etc.
            AddinLog.Warn($"validate-license: falha de rede/serviço — tentando cache offline. Erro: {ex.GetType().Name}: {ex.Message}");
        }

        // ── 2. Fallback: cache offline ────────────────────────────────────────
        var cache = LicenseCache.Read(machineId);

        if (cache != null)
        {
            // Expiração sempre bloqueia, mesmo offline
            if (!string.IsNullOrEmpty(cache.ExpiresAt) &&
                DateTime.TryParse(cache.ExpiresAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var cacheExp) &&
                cacheExp.ToUniversalTime() < DateTime.UtcNow)
            {
                Status = LicenseStatus.Expired;
                AddinLog.Warn($"Licença expirada (cache offline) em {cacheExp:o}.");
                return new(false, "expired");
            }

            // Grace period: até 7 dias desde a última validação online
            if (DateTime.TryParse(cache.ValidatedAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var validatedAt))
            {
                var elapsed  = DateTime.UtcNow - validatedAt.ToUniversalTime();
                var maxGrace = TimeSpan.FromDays(LicensingConfig.GraceOfflineDays);

                if (elapsed <= maxGrace)
                {
                    var remaining = (maxGrace - elapsed).TotalDays;
                    Status    = LicenseStatus.GraceOffline;
                    Plan      = cache.Plan;
                    ExpiresAt = string.IsNullOrEmpty(cache.ExpiresAt) ? null
                        : DateTime.TryParse(cache.ExpiresAt, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var e) ? e.ToUniversalTime() : null;
                    AddinLog.Info(
                        $"Licença em grace offline ({remaining:F1} dia(s) restante(s)). " +
                        $"Plano={Plan}. Último check={validatedAt:o}.");
                    return new(true, "grace_offline") { Plan = Plan, ExpiresAt = ExpiresAt, IsGrace = true };
                }
                else
                {
                    Status = LicenseStatus.Invalid;
                    AddinLog.Warn(
                        $"Grace period expirado ({elapsed.TotalDays:F1} dias sem validação online > {LicensingConfig.GraceOfflineDays}). Bloqueando.");
                    return new(false, "grace_expired");
                }
            }
        }

        // Sem cache e sem rede
        Status = LicenseStatus.Invalid;
        AddinLog.Warn("validate-license: offline sem cache válido — bloqueando.");
        return new(false, "offline_no_cache");
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static async Task<EdgeFunctionResponse?> CallEdgeFunctionAsync(
        string licenseKey, string machineId)
    {
        var url = $"{LicensingConfig.SupabaseUrl}/functions/v1/validate-license";

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", LicensingConfig.SupabaseAnonKey);

        var body = JsonSerializer.Serialize(new
        {
            license_key     = licenseKey,
            machine_id      = machineId,
            product_version = LicensingConfig.ProductVersion,
        });

        using var response = await http.PostAsync(
            url, new StringContent(body, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            AddinLog.Warn($"validate-license: HTTP {(int)response.StatusCode}. Body: {json[..Math.Min(200, json.Length)]}");
        }

        return JsonSerializer.Deserialize<EdgeFunctionResponse>(json, _jsonOpts);
    }

    private static bool VerifySignature(
        string licenseKey, string machineId, string expiresAt, string signature)
    {
        // Em dev mode (secret não configurado) não verifica
        if (LicensingConfig.IsDevMode)
            return true;

        // Sem assinatura na resposta → falha (inesperado em produção)
        if (string.IsNullOrEmpty(signature))
            return false;

        var data = $"{licenseKey}|{machineId}|{expiresAt}";
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(LicensingConfig.LicenseSigningSecret));
        var computed = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLower();

        return CryptographicEquals(computed, signature);
    }

    /// <summary>Comparação em tempo constante para prevenir timing attacks.</summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
