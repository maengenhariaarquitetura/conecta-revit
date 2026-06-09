using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConectaRevit.Addin.Logging;

namespace ConectaRevit.Addin.Licensing;

// Dados persistidos no cache local de licença.
internal sealed record LicenseCacheData
{
    [JsonPropertyName("valid")]       public bool   Valid       { get; init; }
    [JsonPropertyName("plan")]        public string Plan        { get; init; } = "";
    [JsonPropertyName("expiresAt")]   public string ExpiresAt   { get; init; } = "";
    [JsonPropertyName("validatedAt")] public string ValidatedAt { get; init; } = "";
    [JsonPropertyName("machineId")]   public string MachineId   { get; init; } = "";
    [JsonPropertyName("signature")]   public string Signature   { get; init; } = "";
}

/// <summary>
/// Lê e grava o cache de licença em %AppData%\ConectaRevit\license.cache.
///
/// Formato do arquivo:
///   base64(HMAC-SHA256-do-payload) + "." + base64(XOR-obfuscated-JSON)
///
/// A obfuscação XOR impede edição trivial de texto; o HMAC com
/// LicenseSigningSecret torna adulteração detectável.
/// </summary>
internal static class LicenseCache
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConectaRevit", "license.cache");

    // ── Escrita ───────────────────────────────────────────────────────────────

    internal static void Write(LicenseCacheData data)
    {
        try
        {
            var json   = JsonSerializer.Serialize(data);
            var xored  = Xor(Encoding.UTF8.GetBytes(json), LicensingConfig.CacheObfuscationKey);
            var mac    = ComputeMac(xored);
            var content = Convert.ToBase64String(mac) + "." + Convert.ToBase64String(xored);

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, content, Encoding.ASCII);
            AddinLog.Info("license.cache: cache atualizado.");
        }
        catch (Exception ex)
        {
            AddinLog.Warn($"license.cache: falha ao gravar — {ex.Message}");
        }
    }

    // ── Leitura ───────────────────────────────────────────────────────────────

    /// <param name="expectedMachineId">
    ///   Se não for string vazia, verifica se o campo MachineId do cache bate.
    ///   Passa string.Empty para pular a verificação.
    /// </param>
    internal static LicenseCacheData? Read(string expectedMachineId)
    {
        if (!File.Exists(CachePath)) return null;

        try
        {
            var content = File.ReadAllText(CachePath, Encoding.ASCII).Trim();
            var dot     = content.IndexOf('.');
            if (dot < 0)
            {
                AddinLog.Warn("license.cache: formato inválido.");
                return null;
            }

            var mac   = Convert.FromBase64String(content[..dot]);
            var xored = Convert.FromBase64String(content[(dot + 1)..]);

            // Verificação de integridade (HMAC)
            if (!LicensingConfig.IsDevMode)
            {
                var expected = ComputeMac(xored);
                if (!expected.SequenceEqual(mac))
                {
                    AddinLog.Warn("license.cache: HMAC inválido — arquivo adulterado. Ignorando.");
                    return null;
                }
            }

            var json = Encoding.UTF8.GetString(Xor(xored, LicensingConfig.CacheObfuscationKey));
            var data = JsonSerializer.Deserialize<LicenseCacheData>(json);
            if (data == null) return null;

            // Verificação de machine_id
            if (!string.IsNullOrEmpty(expectedMachineId) && data.MachineId != expectedMachineId)
            {
                AddinLog.Warn("license.cache: machine_id não corresponde — ignorando.");
                return null;
            }

            return data;
        }
        catch (Exception ex)
        {
            AddinLog.Warn($"license.cache: falha ao ler — {ex.Message}");
            return null;
        }
    }

    // ── Exclusão ──────────────────────────────────────────────────────────────

    internal static void Delete()
    {
        try
        {
            if (File.Exists(CachePath)) File.Delete(CachePath);
            AddinLog.Info("license.cache: cache removido.");
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] ComputeMac(byte[] payload)
    {
        // Em dev mode usa string fixa (não importa, pois verificação é pulada).
        var secret = LicensingConfig.IsDevMode ? "dev" : LicensingConfig.LicenseSigningSecret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(payload);
    }

    private static byte[] Xor(byte[] data, byte[] key)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        return result;
    }
}
