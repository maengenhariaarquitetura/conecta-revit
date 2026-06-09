using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConectaRevit.Addin.Settings;

// Persiste e lê configurações em %AppData%\ConectaRevit\settings.json (ARCHITECTURE § 5.8).
internal sealed class SettingsManager
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConectaRevit");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private SettingsData _data = new();

    public string Mode    => _data.Mode;
    public int    Port    => _data.Port;
    public string LicenseKey => _data.LicenseKey;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath), JsonOpts)
                        ?? new SettingsData();
        }
        catch
        {
            // Mantém defaults em caso de arquivo corrompido.
            _data = new SettingsData();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, JsonOpts));
    }

    // Usado internamente para alterar mode (Fase 3: botão Configurações).
    internal void SetMode(string mode)
    {
        _data = _data with { Mode = mode };
        Save();
    }

    // Persiste a chave de licença. Chamado pelo LicenseKeyDialog.
    internal void SetLicenseKey(string key)
    {
        _data = _data with { LicenseKey = key };
        Save();
    }
}

// Dados persistidos — campos conforme ARCHITECTURE § 5.8.
// record: suporta expressão `with { }` (CS8858 com class) e é idiomático para DTOs imutáveis.
internal sealed record SettingsData
{
    [JsonPropertyName("mode")]       public string Mode       { get; init; } = "safe";
    [JsonPropertyName("port")]       public int    Port       { get; init; } = 8765;
    [JsonPropertyName("licenseKey")] public string LicenseKey { get; init; } = "";
    [JsonPropertyName("language")]   public string Language   { get; init; } = "pt-BR";
}
