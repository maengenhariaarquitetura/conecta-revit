namespace ConectaRevit.Addin.Logging;

/// <summary>
/// Logger em arquivo para o add-in.
///
/// O add-in não tem stdout/stderr visíveis durante execução dentro do Revit.
/// Este logger grava em %AppData%\ConectaRevit\addin.log com timestamps.
///
/// Thread-safe: lock por escrita de linha.
/// Nunca lança exceção para o chamador (erros de I/O são silenciados).
///
/// Arquivo: %AppData%\ConectaRevit\addin.log
/// </summary>
internal static class AddinLog
{
    private static readonly string _logPath;
    private static readonly object _lock = new();

    static AddinLog()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConectaRevit");

        try { Directory.CreateDirectory(dir); } catch { }

        _logPath = Path.Combine(dir, "addin.log");
    }

    internal static string LogPath => _logPath;

    internal static void Info(string msg)  => Write("INFO ", msg);
    internal static void Warn(string msg)  => Write("WARN ", msg);
    internal static void Error(string msg) => Write("ERROR", msg);

    internal static void Write(string level, string msg)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
            lock (_lock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch { /* nunca falhar por causa de log */ }
    }
}
