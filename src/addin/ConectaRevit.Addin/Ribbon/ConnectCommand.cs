using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ConectaRevit.Addin.Ribbon;

// Comando do botão "Conectar/Desconectar": liga ou desliga o servidor WebSocket.
// Fase 2: sem validação de licença (Fase 5). Conectar sobe direto.
[Transaction(TransactionMode.Manual)]
internal sealed class ConnectCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var server = Application.Server;
        if (server == null)
        {
            TaskDialog.Show("ConectaRevit", "Servidor não inicializado. Reinicie o Revit.");
            return Result.Failed;
        }

        try
        {
            if (server.IsRunning)
            {
                // Start() é síncrono; Stop() também — nenhum Task.Run nem Wait() aqui.
                server.Stop();
                TaskDialog.Show("ConectaRevit", "Servidor desconectado.");
            }
            else
            {
                server.Start();
                TaskDialog.Show("ConectaRevit",
                    $"Servidor conectado e aguardando a ponte MCP.\n" +
                    $"Versão do Revit: {Application.RevitVersion}");
            }

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
