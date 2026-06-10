using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ConectaRevit.Addin.Licensing;

namespace ConectaRevit.Addin.Ribbon;

/// <summary>
/// Resultado de cada verificação de pré-requisito.
/// </summary>
/// <param name="Ok">true = requisito satisfeito (✅), false = problema (❌).</param>
/// <param name="Label">Rótulo exibido para o usuário.</param>
/// <param name="Detail">Detalhe adicional exibido quando <see cref="Ok"/> = true (ex.: versão).</param>
/// <param name="HelpText">Instrução de correção, mostrada em laranja quando <see cref="Ok"/> = false.</param>
internal readonly record struct RequirementItem(
    bool    Ok,
    string  Label,
    string? Detail   = null,
    string? HelpText = null);

/// <summary>
/// Janela WPF com lista visual ✅/❌ dos pré-requisitos do ConectaRevit.
/// Apenas leitura — não executa ações corretivas.
/// </summary>
internal partial class CheckRequirementsDialog : Window
{
    internal CheckRequirementsDialog(IEnumerable<RequirementItem> items, IntPtr ownerHandle)
    {
        InitializeComponent();

        if (ownerHandle != IntPtr.Zero)
            new WindowInteropHelper(this) { Owner = ownerHandle };

        PopulateItems(items);
    }

    // ── Constrói os itens visualmente ────────────────────────────────────────

    private void PopulateItems(IEnumerable<RequirementItem> items)
    {
        bool first = true;
        foreach (var item in items)
        {
            if (!first)
            {
                RequirementsPanel.Children.Add(new Separator
                {
                    Margin = new Thickness(0, 6, 0, 0),
                });
            }
            first = false;

            // Linha: [ícone] [textos]
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 6, 0, 0),
            };

            // Ícone ✅/❌
            row.Children.Add(new TextBlock
            {
                Text              = item.Ok ? "✅" : "❌",
                Width             = 28,
                VerticalAlignment = VerticalAlignment.Top,
                FontSize          = 14,
            });

            // Coluna de texto
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

            // Rótulo principal
            var labelBlock = new TextBlock
            {
                FontWeight   = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Text         = item.Label,
            };
            if (!string.IsNullOrEmpty(item.Detail))
                labelBlock.Text += $"  ({item.Detail})";
            textStack.Children.Add(labelBlock);

            // Instrução de correção (apenas quando ❌)
            if (!item.Ok && !string.IsNullOrEmpty(item.HelpText))
            {
                textStack.Children.Add(new TextBlock
                {
                    Text         = $"→ {item.HelpText}",
                    Foreground   = Brushes.OrangeRed,
                    FontSize     = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 2, 0, 0),
                });
            }

            row.Children.Add(textStack);
            RequirementsPanel.Children.Add(row);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Fábrica de itens (lógica de verificação) ──────────────────────────────

    /// <summary>
    /// Executa todas as verificações de pré-requisito e retorna os resultados.
    /// Chamado por <see cref="CheckRequirementsCommand"/> antes de abrir o diálogo.
    /// </summary>
    internal static IReadOnlyList<RequirementItem> RunChecks()
    {
        var items = new List<RequirementItem>();

        // ── a) Claude Desktop instalado ───────────────────────────────────────
        var appData      = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var claudePaths = new[]
        {
            Path.Combine(appData,      "Claude"),
            Path.Combine(localAppData, "Programs",       "Claude"),
            Path.Combine(localAppData, "AnthropicClaude"),
            Path.Combine(localAppData, "Programs",       "claude-desktop"),
        };
        var claudeOk = claudePaths.Any(Directory.Exists) ||
                       File.Exists(Path.Combine(localAppData, "Programs", "Claude", "Claude.exe"));

        items.Add(new RequirementItem(
            Ok:       claudeOk,
            Label:    "Claude Desktop instalado",
            HelpText: claudeOk ? null
                : "Instale em: https://claude.ai/download"));

        // ── b) Licença ────────────────────────────────────────────────────────
        var license = Application.License;
        var licenseOk = license?.IsConnectAllowed ?? false;
        var licenseUnknown = license?.Status is LicenseStatus.Unknown;

        string? licenseDetail = null;
        string? licenseHelp   = null;

        if (license != null)
        {
            licenseDetail = license.Status switch
            {
                LicenseStatus.Valid        => $"válida até {license.ExpiresAt?.ToLocalTime():dd/MM/yyyy}",
                LicenseStatus.GraceOffline => "grace offline",
                LicenseStatus.Expired      => "expirada",
                LicenseStatus.Invalid      => "inválida",
                LicenseStatus.NoKey        => "não configurada",
                _                          => "não verificada",
            };
            if (!licenseOk)
            {
                licenseHelp = license.Status switch
                {
                    LicenseStatus.NoKey     => "Insira sua chave em Configurações → Gerenciar Licença…",
                    LicenseStatus.Expired   => "Licença expirada. Renove seu plano.",
                    LicenseStatus.Invalid   => "Chave inválida. Verifique em Configurações → Gerenciar Licença…",
                    LicenseStatus.Unknown   => "Clique em Conectar para verificar a licença.",
                    _                       => "Verifique a licença em Configurações → Gerenciar Licença…",
                };
            }
        }

        items.Add(new RequirementItem(
            Ok:       licenseOk || licenseUnknown,   // Unknown = ainda não tentou; não é erro definitivo
            Label:    "Licença válida",
            Detail:   licenseDetail,
            HelpText: licenseHelp));

        // ── c) Porta do servidor disponível ───────────────────────────────────
        var server = Application.Server;
        bool portOk;
        string portDetail;
        string? portHelp = null;

        if (server?.IsRunning == true)
        {
            portOk     = true;
            portDetail = $"porta {server.Port} em uso pelo ConectaRevit";
        }
        else
        {
            portOk     = false;
            portDetail = "";
            for (int p = 8765; p <= 8775; p++)
            {
                try
                {
                    var tcp = new TcpListener(System.Net.IPAddress.Loopback, p);
                    tcp.Start();
                    tcp.Stop();
                    portOk     = true;
                    portDetail = $"porta {p} livre";
                    break;
                }
                catch { /* porta ocupada, tenta a próxima */ }
            }
            if (!portOk)
                portHelp =
                    "Todas as portas 8765–8775 estão ocupadas. " +
                    "Feche outras instâncias do ConectaRevit ou reinicie o Revit.";
        }

        items.Add(new RequirementItem(
            Ok:       portOk,
            Label:    "Porta do servidor disponível",
            Detail:   portDetail,
            HelpText: portHelp));

        // ── d) Arquivo .mcpb instalado ────────────────────────────────────────
        var crDir    = Path.Combine(appData, "ConectaRevit");
        var mcpbFiles = Directory.Exists(crDir)
            ? Directory.GetFiles(crDir, "*.mcpb")
            : [];
        var mcpbOk = mcpbFiles.Length > 0;

        items.Add(new RequirementItem(
            Ok:       mcpbOk,
            Label:    "Pacote de integração (.mcpb) instalado",
            Detail:   mcpbOk ? Path.GetFileName(mcpbFiles[0]) : null,
            HelpText: mcpbOk ? null
                : "Reinstale o ConectaRevit. O arquivo deve estar em %AppData%\\ConectaRevit\\"));

        // ── e) Servidor ligado / WebSocket ativo ──────────────────────────────
        var connected = server?.IsRunning ?? false;
        items.Add(new RequirementItem(
            Ok:       connected,
            Label:    "Servidor ConectaRevit ligado",
            Detail:   connected ? "WebSocket pronto para a ponte MCP" : null,
            HelpText: connected ? null
                : "Clique em 'Conectar' no painel ConectaRevit."));

        return items;
    }
}
