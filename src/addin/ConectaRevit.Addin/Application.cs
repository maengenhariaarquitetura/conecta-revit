using System.Runtime.CompilerServices;
using Autodesk.Revit.UI;
using ConectaRevit.Addin.Execution;
using ConectaRevit.Addin.Logging;
using ConectaRevit.Addin.Ribbon;
using ConectaRevit.Addin.Server;
using ConectaRevit.Addin.Settings;

namespace ConectaRevit.Addin;

// Ponto de entrada do add-in. Responsável por inicializar todos os subsistemas (ARCHITECTURE § 5).
public class Application : IExternalApplication
{
    // GUID estável — não alterar após primeira instalação em campo.
    public static readonly Guid AddInGuid = new("a7f3c2e1-9b4d-4e6a-8c5f-2d1b3a6e9f04");

    // Versão do protocolo WebSocket (PROTOCOL.md § 0). Muda somente com breaking changes.
    internal const string ProtocolVersion = "1.0";

    // Versão do produto — carimbada pelo build.ps1 a partir do arquivo VERSION.
    internal const string AddinVersion = "0.1.0";

    // ─── Statics acessíveis pelos outros subsistemas ─────────────────────────

    // Versão do Revit cacheada no OnStartup. String imutável, segura de ler em qualquer thread.
    internal static string RevitVersion { get; private set; } = "";

    // UIApplication cacheada no Execute() do ExternalEvent.
    // volatile garante visibilidade da última atribuição para threads de background (WS).
    // A thread do WS só lê (nunca escreve); a thread principal só escreve.
    private static volatile UIApplication? _uiApplication;
    internal static UIApplication? UiApplication
    {
        get => _uiApplication;
        set => _uiApplication = value;
    }

    internal static ExecutionEngine? Engine         { get; private set; }
    internal static ExternalEvent?  ExecutionEvent  { get; private set; }
    internal static WebSocketServer? Server   { get; private set; }
    internal static SettingsManager? Settings { get; private set; }

    // ─── Ciclo de vida ───────────────────────────────────────────────────────

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            // 1. Versão do Revit — ControlledApplication não exige UIApplication.
            RevitVersion = application.ControlledApplication.VersionNumber;
            AddinLog.Info($"=== ConectaRevit OnStartup. AddinVersion={AddinVersion}, RevitVersion={RevitVersion}. Log: {AddinLog.LogPath} ===");

            // 2. Configurações
            var settings = new SettingsManager();
            settings.Load();
            Settings = settings;

            // 3. Motor de execução + ExternalEvent.
            //
            // REGRA DE OURO DA API DO REVIT:
            //   ExternalEvent.Create() SÓ pode ser chamado em OnStartup ou
            //   em IExternalCommand.Execute — nunca de thread de background,
            //   nunca de Start(), nunca de timers. Criar fora desse contexto
            //   faz Raise() aceitar ("Accepted") mas Execute() nunca disparar.
            //
            // ExecutionEvent é também guardado em static próprio para que o GC
            // não possa coletar o objeto mesmo que a referência interna do engine
            // fosse a única (cintura E suspensório).
            var engine = new ExecutionEngine(RevitVersion);
            var externalEvent = ExternalEvent.Create(engine);
            engine.SetExternalEvent(externalEvent);
            Engine         = engine;
            ExecutionEvent = externalEvent;   // segundo strong-ref — nunca pode ser GCado

            // HashCodes de identidade (RuntimeHelpers.GetHashCode = endereço-base,
            // imutável, não sobrescrito pelo tipo). DEVEM ser idênticos aos logados
            // em EnqueueAsync e Execute() para provar instância única.
            AddinLog.Info(
                $"ExternalEvent criado em OnStartup. " +
                $"Thread={Environment.CurrentManagedThreadId} (esperado: Thread 1). " +
                $"EngineHashCode={RuntimeHelpers.GetHashCode(engine)}. " +
                $"EventHashCode={RuntimeHelpers.GetHashCode(externalEvent)}. " +
                $"IsPending={externalEvent.IsPending}.");

            // 4. Servidor WebSocket — passa a MESMA instância do engine criada acima.
            // WebSocketServer._engine == Application.Engine == handler do ExternalEvent.

            Server = new WebSocketServer(engine, settings);

            // 5. Smoke test Roslyn — ANTES do primeiro uso do ExternalEvent.
            //
            // Verifica se Microsoft.CodeAnalysis.* carregam sem conflito de versão.
            // Se falhar, o log mostrará o TypeLoadException/FileLoadException que
            // impede DispatchJob() de rodar quando o ExternalEvent é despachado.
            //
            // O try/catch aqui captura falhas de JIT do próprio RunSmokeTest()
            // (que contém tipos Roslyn em seu IL — mesmo risco que DispatchJob).
            AddinLog.Info("Startup: chamando RoslynSmokeTest...");
            try { ExecutionEngine.RunSmokeTest(); }
            catch (Exception exSmoke)
            {
                AddinLog.Error(
                    $"RoslynSmokeTest lançou exceção ao ser JIT-compilado " +
                    $"(TypeLoadException/FileLoadException de DLL Roslyn): " +
                    $"{ExecutionEngine.BuildExceptionDiag(exSmoke)}");
            }

            // 6. Ribbon
            var panel = new ConectaRibbon(Server);
            panel.Register(application);

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error($"OnStartup: falha na inicialização: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            TaskDialog.Show("ConectaRevit — Erro na inicialização", ex.Message);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        AddinLog.Info("=== ConectaRevit OnShutdown. ===");
        try { Server?.Stop(); } catch { }
        return Result.Succeeded;
    }
}
