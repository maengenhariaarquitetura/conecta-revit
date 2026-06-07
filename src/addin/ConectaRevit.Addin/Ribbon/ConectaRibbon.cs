using Autodesk.Revit.UI;
using ConectaRevit.Addin.Server;
using ConectaRevit.Addin.Settings;

namespace ConectaRevit.Addin.Ribbon;

// Cria o painel "ConectaRevit" no ribbon do Revit (ARCHITECTURE § 5.1).
// Renomeada de RibbonPanel para ConectaRibbon: evita colisão com Autodesk.Revit.UI.RibbonPanel (CS0104).
//
// Fase 3D: adicionado botão "Configurações" com exibição do modo atual no tooltip.
// _settingsBtn é static para que SettingsCommand.Execute() possa atualizar o tooltip
// sem precisar de uma referência à instância de ConectaRibbon.
internal sealed class ConectaRibbon
{
    private readonly WebSocketServer _server;

    // Referência ao botão Configurações: atualizada em Register() e lida em UpdateModeDisplay().
    // Acesso apenas pela Thread 1 (OnStartup e IExternalCommand.Execute) — sem necessidade de lock.
    private static PushButton? _settingsBtn;

    internal ConectaRibbon(WebSocketServer server) => _server = server;

    public void Register(UIControlledApplication application)
    {
        var asm   = typeof(Application).Assembly.Location;
        var panel = application.CreateRibbonPanel("ConectaRevit");

        // ── Botão 1: Conectar / Desconectar ─────────────────────────────────
        var connectData = new PushButtonData(
            name:         "ConectarDesconectar",
            text:         "Conectar",
            assemblyName: asm,
            className:    typeof(ConnectCommand).FullName!
        )
        {
            ToolTip = "Liga ou desliga o servidor WebSocket do ConectaRevit."
        };
        panel.AddItem(connectData);

        // ── Botão 2: Configurações ───────────────────────────────────────────
        var settingsData = new PushButtonData(
            name:         "Configuracoes",
            text:         "Configurações",
            assemblyName: asm,
            className:    typeof(SettingsCommand).FullName!
        )
        {
            ToolTip = "Configura o modo de execução padrão (Seguro ou Direto)."
        };
        _settingsBtn = panel.AddItem(settingsData) as PushButton;

        // Inicializa o tooltip com o modo atual lido do settings (já carregado no OnStartup).
        var initialMode = Application.Settings?.Mode ?? "safe";
        UpdateModeDisplay(initialMode);

        // ── Botões 3–6: TODO fases futuras ───────────────────────────────────
        // Verificar Requisitos, Console, Reverter última ação.
    }

    // Atualiza o tooltip do botão Configurações para refletir o modo atual.
    // Chamado por SettingsCommand após alterar o modo e em Register() na inicialização.
    // Thread-safety: sempre chamado na Thread 1 (IExternalCommand.Execute ou OnStartup).
    internal static void UpdateModeDisplay(string mode)
    {
        if (_settingsBtn == null) return;

        var modeLabel = mode == "direct" ? "Direto (avançado)" : "Seguro (recomendado)";
        _settingsBtn.ToolTip = $"Modo atual: {modeLabel}\nClique para alterar o modo de execução.";
    }
}
