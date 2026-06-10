using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ConectaRevit.Addin.Licensing;

namespace ConectaRevit.Addin.Ribbon;

// Botão "Status": exibe o estado atual do ConectaRevit em leitura.
// Não modifica o documento nem o estado do servidor.
[Transaction(TransactionMode.ReadOnly)]
internal sealed class StatusCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var server   = Application.Server;
        var settings = Application.Settings;
        var license  = Application.License;

        var sb = new StringBuilder();

        // ── Servidor ──────────────────────────────────────────────────────────
        if (server?.IsRunning == true)
            sb.AppendLine($"Servidor:          ✅  Ligado (porta {server.Port})");
        else
            sb.AppendLine("Servidor:          ❌  Desligado — clique em Conectar");

        // ── Documento ativo ───────────────────────────────────────────────────
        var docTitle = commandData.Application.ActiveUIDocument?.Document?.Title;
        sb.AppendLine(string.IsNullOrWhiteSpace(docTitle)
            ? "Documento ativo:   (nenhum)"
            : $"Documento ativo:   {docTitle}");

        // ── Modo de execução ──────────────────────────────────────────────────
        var modeLabel = settings?.Mode == "direct" ? "Direto (avançado)" : "Seguro (recomendado)";
        sb.AppendLine($"Modo de execução:  {modeLabel}");

        // ── Licença ───────────────────────────────────────────────────────────
        sb.AppendLine(LicenseStatusLine(license));

        var dlg = new TaskDialog("ConectaRevit — Status")
        {
            MainInstruction = "Estado atual do ConectaRevit",
            MainContent     = sb.ToString().TrimEnd(),
        };
        dlg.CommonButtons = TaskDialogCommonButtons.Close;
        dlg.Show();

        return Result.Succeeded;
    }

    internal static string LicenseStatusLine(LicenseManager? license)
    {
        if (license == null) return "Licença:           (não disponível)";

        var exp = license.ExpiresAt.HasValue
            ? $" — válida até {license.ExpiresAt.Value.ToLocalTime():dd/MM/yyyy}"
            : "";
        var plan = !string.IsNullOrEmpty(license.Plan) ? $" [{license.Plan}]" : "";

        return license.Status switch
        {
            LicenseStatus.Valid        => $"Licença:           ✅  Válida{exp}{plan}",
            LicenseStatus.GraceOffline => $"Licença:           ⚠️  Grace offline{exp}{plan}",
            LicenseStatus.Expired      => $"Licença:           ❌  Expirada{exp}",
            LicenseStatus.Invalid      => "Licença:           ❌  Inválida",
            LicenseStatus.NoKey        => "Licença:           ❌  Chave não configurada",
            _                          => "Licença:           (não verificada — clique em Conectar)",
        };
    }
}
