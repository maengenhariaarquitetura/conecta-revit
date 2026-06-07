using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ConectaRevit.Addin.Logging;

namespace ConectaRevit.Addin.Ribbon;

// Comando do botão "Configurações": permite ao usuário alternar o modo de execução
// (Seguro ↔ Direto) e persiste a escolha em settings.json via SettingsManager.
//
// O modo padrão vale para execuções sem 'mode' explícito no execute_code.
// Execuções com 'mode' explícito sempre sobrepõem o padrão.
[Transaction(TransactionMode.ReadOnly)]
internal sealed class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var settings = Application.Settings;
        if (settings == null)
        {
            TaskDialog.Show("ConectaRevit", "Configurações não disponíveis. Reinicie o Revit.");
            return Result.Failed;
        }

        var currentMode  = settings.Mode;                                   // "safe" | "direct"
        var currentLabel = ModeLabel(currentMode);

        // ── Diálogo principal ──────────────────────────────────────────────────
        var dlg = new TaskDialog("ConectaRevit — Configurações");
        dlg.MainInstruction = $"Modo de execução atual: {currentLabel}";
        dlg.MainContent =
            "Escolha o modo padrão para execução de código C# enviado pelo Claude:\n\n" +
            "• Seguro (recomendado): o ConectaRevit abre e commita automaticamente " +
            "uma Transaction em volta do código e trata avisos do Revit. " +
            "O código não deve abrir transação própria.\n\n" +
            "• Direto (avançado): o código gerencia as próprias transações " +
            "(using var tx = new Transaction(...)). " +
            "Use apenas se souber o que está fazendo.";

        dlg.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink1,
            "Modo Seguro  (recomendado)",
            "Harness gerencia Transaction + IFailuresPreprocessor automaticamente.");
        dlg.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink2,
            "Modo Direto  (avançado)",
            "Script gerencia as próprias transações; harness suprime apenas diálogos modais.");

        dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
        dlg.DefaultButton = currentMode == "direct"
            ? TaskDialogResult.CommandLink2
            : TaskDialogResult.CommandLink1;

        var picked = dlg.Show();

        string? newMode = picked switch
        {
            TaskDialogResult.CommandLink1 => "safe",
            TaskDialogResult.CommandLink2 => "direct",
            _                             => null        // Cancel ou fechar
        };

        if (newMode == null || newMode == currentMode)
            return Result.Succeeded;

        // ── Confirmação obrigatória ao trocar PARA Modo Direto ─────────────────
        if (newMode == "direct")
        {
            var warn = new TaskDialog("ConectaRevit — Confirmar Modo Direto");
            warn.MainInstruction = "Atenção: Modo Direto exige cuidado extra";
            warn.MainContent =
                "No Modo Direto, o código C# é responsável por abrir, commitar e " +
                "fazer rollback de suas próprias transações (using var tx = new Transaction(...)).\n\n" +
                "Obrigações do script:\n" +
                "  1. Chamar tx.GetFailureHandlingOptions().SetForcedModalHandling(false)\n" +
                "     antes de tx.Start() para evitar diálogos modais bloqueantes.\n" +
                "  2. Tratar falhas de commit e fazer tx.RollBack() nos catch.\n\n" +
                "Sem essas precauções, um aviso do Revit (ex.: paredes sobrepostas) " +
                "pode travar a execução indefinidamente.\n\n" +
                "Deseja continuar?";
            warn.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            warn.DefaultButton  = TaskDialogResult.No;

            if (warn.Show() != TaskDialogResult.Yes)
                return Result.Succeeded;
        }

        // ── Aplicar e persistir ────────────────────────────────────────────────
        AddinLog.Info($"Modo alterado: {currentMode} -> {newMode} (via botão Configurações).");
        settings.SetMode(newMode);

        // Atualizar exibição do ribbon
        ConectaRibbon.UpdateModeDisplay(newMode);

        TaskDialog.Show("ConectaRevit",
            $"Modo alterado para: {ModeLabel(newMode)}\n\n" +
            "As próximas execuções sem 'mode' explícito usarão este modo.");

        return Result.Succeeded;
    }

    private static string ModeLabel(string mode) =>
        mode == "direct" ? "Direto (avançado)" : "Seguro (recomendado)";
}
