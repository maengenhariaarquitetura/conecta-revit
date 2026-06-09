using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ConectaRevit.Addin.Licensing;
using ConectaRevit.Addin.Logging;

namespace ConectaRevit.Addin.Ribbon;

// Comando do botão "Conectar/Desconectar": liga ou desliga o servidor WebSocket.
// Fase 5: valida a licença antes de permitir a conexão.
[Transaction(TransactionMode.Manual)]
internal sealed class ConnectCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var server   = Application.Server;
        var settings = Application.Settings;
        var license  = Application.License;

        if (server == null || settings == null || license == null)
        {
            TaskDialog.Show("ConectaRevit", "Servidor não inicializado. Reinicie o Revit.");
            return Result.Failed;
        }

        // ── Desconectar não exige validação de licença ────────────────────────
        if (server.IsRunning)
        {
            try
            {
                server.Stop();
                TaskDialog.Show("ConectaRevit", "Servidor desconectado.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ConectaRevit — Erro ao desconectar", ex.Message);
                return Result.Failed;
            }
            return Result.Succeeded;
        }

        // ── Conectar: exige licença válida ────────────────────────────────────
        var licenseKey = settings.LicenseKey;

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            TaskDialog.Show(
                "ConectaRevit — Licença",
                "Nenhuma chave de licença configurada.\n\n" +
                "Clique em 'Configurações' no painel do ConectaRevit e acesse " +
                "'Gerenciar Licença…' para inserir sua chave.");
            return Result.Failed;
        }

        // Validação (síncrona: bloqueia a UI thread por ≤ 10s; aceitável para
        // operação única que o usuário iniciou conscientemente).
        LicenseValidationResult result;
        try
        {
            result = license.ValidateAsync(licenseKey).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AddinLog.Error($"ConnectCommand: exceção inesperada na validação de licença: {ex}");
            TaskDialog.Show(
                "ConectaRevit — Erro de licença",
                $"Erro inesperado ao validar licença:\n{ex.Message}");
            return Result.Failed;
        }

        if (!result.Valid)
        {
            var detail = result.Reason switch
            {
                "not_found"          => "Chave não encontrada. Verifique se digitou corretamente.",
                "suspended"          => "Licença suspensa. Entre em contato com o suporte.",
                "expired"            => $"Licença expirada{(result.ExpiresAt.HasValue ? $" em {result.ExpiresAt.Value.ToLocalTime():dd/MM/yyyy}" : "")}. Renove para continuar.",
                "machine_mismatch"   => "Esta chave já está ativada em outra máquina.",
                "grace_expired"      => $"Grace period offline expirado ({LicensingConfig.GraceOfflineDays} dias sem validação online).\nConecte à Internet e tente novamente.",
                "offline_no_cache"   => "Sem conexão com o servidor de licenças e sem cache local.\nConecte à Internet para ativar.",
                "signature_mismatch" => "Falha de verificação de integridade. Contate o suporte.",
                "no_key"             => "Chave de licença não configurada.",
                _                    => $"Licença inválida (código: {result.Reason ?? "?"}). Contate o suporte.",
            };

            TaskDialog.Show("ConectaRevit — Licença inválida", detail);
            return Result.Failed;
        }

        // Aviso de grace offline (permitido, mas informa o usuário)
        if (result.IsGrace)
        {
            var graceMsg = new TaskDialog("ConectaRevit — Grace Offline")
            {
                MainInstruction = "Servidor de licenças temporariamente inacessível",
                MainContent     =
                    $"O servidor de licenças não pôde ser contactado.\n" +
                    $"Funcionando em modo offline por até {LicensingConfig.GraceOfflineDays} dias desde a última validação.\n\n" +
                    $"Plano: {result.Plan ?? "standard"}\n" +
                    (result.ExpiresAt.HasValue
                        ? $"Válida até: {result.ExpiresAt.Value.ToLocalTime():dd/MM/yyyy}\n\n"
                        : "\n") +
                    "Conecte à Internet em breve para revalidar.",
            };
            graceMsg.CommonButtons = TaskDialogCommonButtons.Ok;
            graceMsg.Show();
        }

        // ── Inicia o servidor ─────────────────────────────────────────────────
        try
        {
            server.Start();
            var expInfo = result.ExpiresAt.HasValue
                ? $"\nLicença válida até: {result.ExpiresAt.Value.ToLocalTime():dd/MM/yyyy}"
                : "";
            TaskDialog.Show(
                "ConectaRevit",
                $"Servidor conectado e aguardando a ponte MCP.\n" +
                $"Versão do Revit: {Application.RevitVersion}" +
                expInfo);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            // Start() lança se todas as portas 8765–8775 estiverem ocupadas.
            TaskDialog.Show("ConectaRevit — Erro ao conectar", ex.Message);
            return Result.Failed;
        }
    }
}
