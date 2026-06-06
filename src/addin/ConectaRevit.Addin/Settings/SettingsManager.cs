namespace ConectaRevit.Addin.Settings;

// Persiste e lê as configurações do add-in (ARCHITECTURE § 5.8).
// TODO Fase 3: implementar leitura/escrita de %AppData%\ConectaRevit\settings.json.
//
// Campos persistidos: mode, port, licenseKey, language.
internal sealed class SettingsManager
{
    public string Mode { get; set; } = "safe";
    public int Port { get; set; } = 8765;
    public string LicenseKey { get; set; } = string.Empty;
    public string Language { get; set; } = "pt-BR";

    // TODO Fase 3: implementar Load() e Save() via System.Text.Json.
    public void Load() { }
    public void Save() { }
}
