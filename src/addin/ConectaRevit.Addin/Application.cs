using Autodesk.Revit.UI;

namespace ConectaRevit.Addin;

// TODO Fase 3: inicializar ribbon, ExternalEvent e servidor WebSocket em OnStartup.
public class Application : IExternalApplication
{
    // GUID estável do add-in — não alterar após primeira instalação em campo.
    // Deve coincidir com o AddInId em ConectaRevit.addin e no installer.
    public static readonly Guid AddInGuid = new("a7f3c2e1-9b4d-4e6a-8c5f-2d1b3a6e9f04");

    public Result OnStartup(UIControlledApplication application)
    {
        // TODO Fase 3: Ribbon.RibbonPanel.Register(application)
        // TODO Fase 3: ExecutionEngine.Initialize(uiApp) — deve ocorrer aqui (contexto válido)
        // TODO Fase 3: WebSocketServer.Start()
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        // TODO Fase 3: WebSocketServer.Stop(), liberar ExternalEvent e recursos.
        return Result.Succeeded;
    }
}
