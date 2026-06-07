using Autodesk.Revit.UI;
using ConectaRevit.Addin.Server;

namespace ConectaRevit.Addin.Ribbon;

// Cria o painel "ConectaRevit" no ribbon do Revit (ARCHITECTURE § 5.1).
// Renomeada de RibbonPanel para ConectaRibbon: evita colisão com Autodesk.Revit.UI.RibbonPanel (CS0104).
// Fase 2: apenas o botão "Conectar/Desconectar". Outros 5 botões: TODO Fase 3.
internal sealed class ConectaRibbon
{
    private readonly WebSocketServer _server;

    internal ConectaRibbon(WebSocketServer server) => _server = server;

    public void Register(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel("ConectaRevit");

        // ── Botão 1: Conectar / Desconectar ─────────────────────────────────
        var btnData = new PushButtonData(
            name:         "ConectarDesconectar",
            text:         "Conectar",
            assemblyName: typeof(Application).Assembly.Location,
            className:    typeof(ConnectCommand).FullName!
        )
        {
            ToolTip = "Liga ou desliga o servidor WebSocket do ConectaRevit."
        };
        panel.AddItem(btnData);

        // ── Botões 2–6: TODO Fase 3 ──────────────────────────────────────────
        // Status, Verificar Requisitos, Console, Reverter última ação, Configurações.
    }
}
