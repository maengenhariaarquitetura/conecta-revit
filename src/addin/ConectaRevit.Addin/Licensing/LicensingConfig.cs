namespace ConectaRevit.Addin.Licensing;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  PREENCHA OS VALORES ABAIXO ANTES DE BUILDAR PARA PRODUÇÃO              ║
// ║                                                                          ║
// ║  1. SupabaseUrl        — Project Settings → API → Project URL            ║
// ║  2. SupabaseAnonKey    — Project Settings → API → anon / public          ║
// ║  3. LicenseSigningSecret — qualquer string aleatória forte (≥ 32 chars). ║
// ║     O MESMO valor deve ser configurado na Edge Function como secret:     ║
// ║     Supabase Dashboard → Edge Functions → validate-license → Secrets     ║
// ║     Nome do secret: LICENSE_SIGNING_SECRET                               ║
// ╚══════════════════════════════════════════════════════════════════════════╝
internal static class LicensingConfig
{
    // ── Supabase ──────────────────────────────────────────────────────────────
    internal const string SupabaseUrl     = "https://cxdgfelwdljysqrptebr.supabase.co";
    internal const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImN4ZGdmZWx3ZGxqeXNxcnB0ZWJyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODA5MDI3NTYsImV4cCI6MjA5NjQ3ODc1Nn0.5atZWXEXf9smS2B8xa9_Ey5wkg-hiPpsVAa5lv_ZD6g";

    // ── Assinatura HMAC ───────────────────────────────────────────────────────
    // Deve bater com LICENSE_SIGNING_SECRET no Supabase Edge Function.
    // Gere com: python -c "import secrets; print(secrets.token_hex(32))"
    internal const string LicenseSigningSecret = "d4a1b6e825c30f79";

    // ── Grace offline ─────────────────────────────────────────────────────────
    internal const int GraceOfflineDays = 7;

    // ── Versão do produto (espelha Application.AddinVersion) ─────────────────
    internal const string ProductVersion = Application.AddinVersion;

    // ── Obfuscação do cache (XOR key de 16 bytes) ─────────────────────────────
    // Não é segredo criptográfico — apenas impede edição trivial de texto puro.
    // O HMAC de integridade (com LicenseSigningSecret) é o que protege o arquivo.
    internal static readonly byte[] CacheObfuscationKey =
    [
        0x43, 0x52, 0x76, 0x31, 0x4C, 0x69, 0x63, 0x3A,
        0x8F, 0x2E, 0xA1, 0x54, 0x77, 0xB3, 0xC0, 0x19,
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna true enquanto o signing secret ainda é o placeholder.
    /// Nesse modo o HMAC é ignorado (facilita desenvolvimento local).
    /// </summary>
    internal static bool IsDevMode =>
        string.IsNullOrWhiteSpace(LicenseSigningSecret) ||
        LicenseSigningSecret.StartsWith("SUBSTITUA");
}
