using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using ConectaRevit.Addin.Server;
using ConectaRevit.Addin.Settings;

namespace ConectaRevit.Addin.Ribbon;

// Cria o painel "ConectaRevit" no ribbon do Revit (ARCHITECTURE § 5.1).
//
// Ordem dos botões:
//   1. Conectar / Desconectar
//   2. Status
//   3. Verificar Requisitos
//   4. Configurações
//
// ÍCONES — quando você tiver os PNGs (16×16 e 32×32), procure o método
// LoadIcon() no final deste arquivo. Cada botão tem duas chamadas comentadas
// (LargeImage = 32×32, Image = 16×16) marcadas com o rótulo "ÍCONE:".
// Adicione os PNGs como EmbeddedResource no .csproj e ajuste o caminho no
// LoadIcon() — as linhas de atribuição já existem e só precisam do nome correto.
internal sealed class ConectaRibbon
{
    private readonly WebSocketServer _server;

    // Referência ao botão Configurações: atualizada em Register() e lida em UpdateModeDisplay().
    // Acesso apenas pela Thread 1 (OnStartup e IExternalCommand.Execute).
    private static PushButton? _settingsBtn;

    internal ConectaRibbon(WebSocketServer server) => _server = server;

    public void Register(UIControlledApplication application)
    {
        var asm   = typeof(Application).Assembly.Location;
        var panel = application.CreateRibbonPanel("ConectaRevit");

        // ╔══════════════════════════════════════════════════════════════╗
        // ║  BOTÃO 1 — Conectar / Desconectar                           ║
        // ╚══════════════════════════════════════════════════════════════╝
        var connectData = new PushButtonData(
            name:         "ConectarDesconectar",
            text:         "Conectar",
            assemblyName: asm,
            className:    typeof(ConnectCommand).FullName!)
        {
            ToolTip = "Liga ou desliga o servidor WebSocket do ConectaRevit.",
            // ÍCONE: Conectar — descomente e preencha quando tiver os PNGs:
            LargeImage = LoadIcon("conectar_32.png"),   // 32×32
            Image      = LoadIcon("conectar_16.png"),   // 16×16
        };
        panel.AddItem(connectData);

        // ╔══════════════════════════════════════════════════════════════╗
        // ║  BOTÃO 2 — Status                                           ║
        // ╚══════════════════════════════════════════════════════════════╝
        var statusData = new PushButtonData(
            name:         "Status",
            text:         "Status",
            assemblyName: asm,
            className:    typeof(StatusCommand).FullName!)
        {
            ToolTip = "Exibe o estado atual: servidor, documento, modo e licença.",
            // ÍCONE: Status — descomente e preencha quando tiver os PNGs:
            LargeImage = LoadIcon("status_32.png"),     // 32×32
            Image      = LoadIcon("status_16.png"),     // 16×16
        };
        panel.AddItem(statusData);

        // ╔══════════════════════════════════════════════════════════════╗
        // ║  BOTÃO 3 — Verificar Requisitos                             ║
        // ╚══════════════════════════════════════════════════════════════╝
        var checkData = new PushButtonData(
            name:         "VerificarRequisitos",
            text:         "Verificar\nRequisitos",
            assemblyName: asm,
            className:    typeof(CheckRequirementsCommand).FullName!)
        {
            ToolTip = "Verifica os pré-requisitos para o ConectaRevit funcionar (Claude Desktop, licença, porta…).",
            // ÍCONE: Verificar Requisitos — descomente e preencha quando tiver os PNGs:
            LargeImage = LoadIcon("requisitos_32.png"), // 32×32
            Image      = LoadIcon("requisitos_16.png"), // 16×16
        };
        panel.AddItem(checkData);

        // ╔══════════════════════════════════════════════════════════════╗
        // ║  BOTÃO 4 — Configurações                                    ║
        // ╚══════════════════════════════════════════════════════════════╝
        var settingsData = new PushButtonData(
            name:         "Configuracoes",
            text:         "Configurações",
            assemblyName: asm,
            className:    typeof(SettingsCommand).FullName!)
        {
            ToolTip = "Modo de execução e gerenciamento de licença.",
            // ÍCONE: Configurações — descomente e preencha quando tiver os PNGs:
            LargeImage = LoadIcon("config_32.png"),     // 32×32
            Image      = LoadIcon("config_16.png"),     // 16×16
        };
        _settingsBtn = panel.AddItem(settingsData) as PushButton;

        // Inicializa o tooltip com o modo atual (lido do settings no OnStartup).
        UpdateModeDisplay(Application.Settings?.Mode ?? "safe");
    }

    // ── Atualização de tooltip ────────────────────────────────────────────────

    // Atualiza o tooltip do botão Configurações para refletir o modo atual.
    // Chamado por SettingsCommand.Execute() após alterar o modo e em Register().
    // Thread-safety: sempre chamado na Thread 1.
    internal static void UpdateModeDisplay(string mode)
    {
        if (_settingsBtn == null) return;
        var label = mode == "direct" ? "Direto (avançado)" : "Seguro (recomendado)";
        _settingsBtn.ToolTip =
            $"Modo atual: {label}\n" +
            "Clique para alterar o modo de execução ou gerenciar a licença.";
    }

    // ── Carregamento de ícones ────────────────────────────────────────────────

    /// <summary>
    /// Carrega um PNG embutido no assembly como <see cref="ImageSource"/> para o ribbon.
    ///
    /// Os PNGs devem estar em:
    ///   src/addin/ConectaRevit.Addin/Resources/Icons/&lt;nome&gt;.png
    ///
    /// O SDK compila cada arquivo PNG dessa pasta como EmbeddedResource com nome:
    ///   ConectaRevit.Addin.Resources.Icons.&lt;nome&gt;.png
    ///
    /// PARA ATIVAR OS ÍCONES APÓS DROPAR OS PNGs:
    ///   1. Salve os 8 PNGs em  Resources/Icons/  (o .csproj já os inclui via glob).
    ///   2. Descomente as linhas  // LargeImage = LoadIcon(...)  e  // Image = LoadIcon(...)
    ///      nos 4 blocos de botão em Register() acima.
    ///   3. Rebuilde — sem mais alterações necessárias.
    /// </summary>
    /// <summary>
    /// Carrega um PNG embutido como WPF Resource via pack URI.
    ///
    /// URI gerado: pack://application:,,,/ConectaRevit.Addin;component/Resources/Icons/{iconName}
    ///
    /// O WPF empacota os itens <![CDATA[<Resource Include="Resources\Icons\*.png">]]> dentro de
    /// ConectaRevit.Addin.g.resources e os expõe por esse URI — que é como projetos
    /// WPF Library embarcam imagens corretamente (diferente de EmbeddedResource, que é
    /// interceptado pelo target AssignWinFXEmbeddedResource e fica inacessível via stream).
    ///
    /// Retorna null se o PNG ainda não existir — o Revit aceita ícone null e mostra
    /// o botão sem imagem, sem lançar exceção.
    /// </summary>
    private static ImageSource? LoadIcon(string iconName)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource   = new Uri(
                $"pack://application:,,,/ConectaRevit.Addin;component/Resources/Icons/{iconName}");
            img.CacheOption = BitmapCacheOption.OnLoad; // carrega tudo na memória agora
            img.EndInit();
            img.Freeze();   // torna imutável e seguro para uso entre threads do Revit
            return img;
        }
        catch
        {
            // PNG não encontrado ou URI inválido → botão sem ícone (aceitável no Revit)
            return null;
        }
    }
}
