using Autodesk.Revit.UI;
using ConectaRevit.Addin.Execution;
using ConectaRevit.Addin.Ribbon;
using ConectaRevit.Addin.Server;
using ConectaRevit.Addin.Settings;

namespace ConectaRevit.Addin;

// Ponto de entrada do add-in. Responsável por inicializar todos os subsistemas (ARCHITECTURE § 5).
public class Application : IExternalApplication
{
    // GUID estável — não alterar após primeira instalação em campo.
    public static readonly Guid AddInGuid = new("a7f3c2e1-9b4d-4e6a-8c5f-2d1b3a6e9f04");

    // Versão do protocolo WebSocket (PROTOCOL.md § 0). Deve mudar apenas com breaking changes.
    internal const string ProtocolVersion = "1.0";

    // Versão do produto — carimbada pelo build.ps1 a partir do arquivo VERSION.
    internal const string AddinVersion = "0.1.0";

    // ─── Statics acessíveis pelos outros subsistemas ─────────────────────────

    // Versão do Revit obtida de ControlledApplication em OnStartup e cacheada aqui.
    // Seguro de ler de qualquer thread (string é imutável; atribuição em OnStartup antes de subir o WS).
    internal static string RevitVersion { get; private set; } = "";

    internal static ExecutionEngine? Engine  { get; private set; }
    internal static WebSocketServer? Server  { get; private set; }
    internal static SettingsManager? Settings { get; private set; }

    // ─── Ciclo de vida ───────────────────────────────────────────────────────

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            // 1. Versão do Revit — ControlledApplication não exige UIApplication nem contexto de API.
            RevitVersion = application.ControlledApplication.VersionNumber;

            // 2. Configurações
            var settings = new SettingsManager();
            settings.Load();
            Settings = settings;

            // 3. Motor de execução + ExternalEvent (deve ser criado aqui, em OnStartup)
            var engine = new ExecutionEngine(RevitVersion);
            var externalEvent = ExternalEvent.Create(engine);
            engine.SetExternalEvent(externalEvent);
            Engine = engine;

            // 4. Servidor WebSocket (não sobe automaticamente — aguarda o botão "Conectar")
            Server = new WebSocketServer(engine, settings);

            // 5. Ribbon (1 botão Conectar/Desconectar nesta fase)
            var panel = new ConectaRibbon(Server);
            panel.Register(application);

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("ConectaRevit — Erro na inicialização", ex.Message);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try { Server?.Stop(); } catch { /* ignora erros no shutdown */ }
        return Result.Succeeded;
    }
}
