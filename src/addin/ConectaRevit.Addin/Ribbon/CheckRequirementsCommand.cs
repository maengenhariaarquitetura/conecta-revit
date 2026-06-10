using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ConectaRevit.Addin.Ribbon;

// Botão "Verificar Requisitos": abre o diálogo de diagnóstico com ✅/❌ por item.
// Apenas leitura — não modifica documento nem estado do servidor.
[Transaction(TransactionMode.ReadOnly)]
internal sealed class CheckRequirementsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var items       = CheckRequirementsDialog.RunChecks();
        var ownerHandle = commandData.Application.MainWindowHandle;
        var dialog      = new CheckRequirementsDialog(items, ownerHandle);
        dialog.ShowDialog();
        return Result.Succeeded;
    }
}
