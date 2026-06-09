using System.Windows;
using System.Windows.Interop;
using ConectaRevit.Addin.Settings;

namespace ConectaRevit.Addin.Licensing;

/// <summary>
/// Diálogo WPF para inserção e validação da chave de licença.
/// Aberto pelo SettingsCommand quando o usuário clica "Gerenciar Licença…".
/// </summary>
internal partial class LicenseKeyDialog : Window
{
    private readonly LicenseManager  _license;
    private readonly SettingsManager _settings;

    internal LicenseKeyDialog(LicenseManager license, SettingsManager settings, IntPtr ownerHandle)
    {
        _license  = license;
        _settings = settings;

        InitializeComponent();

        // Ancora o diálogo na janela principal do Revit
        if (ownerHandle != IntPtr.Zero)
            new WindowInteropHelper(this) { Owner = ownerHandle };

        // Inicializa campos
        TxtKey.Text       = _settings.LicenseKey;
        TxtMachineId.Text = $"ID da máquina: {_license.GetMachineId()[..16]}…  (para suporte técnico)";
        RefreshStatus();
    }

    // ── Exibição de status ────────────────────────────────────────────────────

    private void RefreshStatus()
    {
        TxtStatus.Text       = BuildStatusText(_license.Status, _license.ExpiresAt, _license.Plan);
        TxtStatus.Foreground = _license.Status switch
        {
            LicenseStatus.Valid        => System.Windows.Media.Brushes.DarkGreen,
            LicenseStatus.GraceOffline => System.Windows.Media.Brushes.DarkOrange,
            LicenseStatus.NoKey        => System.Windows.Media.Brushes.Gray,
            _                          => System.Windows.Media.Brushes.Crimson,
        };
    }

    private static string BuildStatusText(LicenseStatus status, DateTime? expiresAt, string plan)
    {
        var exp = expiresAt.HasValue ? $" — válida até {expiresAt.Value.ToLocalTime():dd/MM/yyyy}" : "";
        var planLabel = string.IsNullOrEmpty(plan) ? "" : $" (plano: {plan})";
        return status switch
        {
            LicenseStatus.Valid        => $"✅ Licença válida{exp}{planLabel}",
            LicenseStatus.GraceOffline => $"⚠️ Grace offline — sem conexão com servidor{exp}{planLabel}",
            LicenseStatus.Expired      => $"❌ Licença expirada{exp}",
            LicenseStatus.Invalid      => "❌ Licença inválida",
            LicenseStatus.NoKey        => "Nenhuma chave configurada",
            _                          => "Não verificada nesta sessão",
        };
    }

    // ── Eventos ───────────────────────────────────────────────────────────────

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var newKey = TxtKey.Text.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(newKey))
        {
            TxtStatus.Text       = "❌ Insira uma chave antes de salvar.";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Crimson;
            return;
        }

        // Desabilita botão durante validação
        BtnSave.IsEnabled       = false;
        BtnSave.Content         = "Validando…";
        TxtStatus.Text          = "⏳ Validando chave…";
        TxtStatus.Foreground    = System.Windows.Media.Brushes.DimGray;

        try
        {
            // Persiste antes de validar (para que ValidateAsync use a chave nova)
            _settings.SetLicenseKey(newKey);
            TxtKey.Text = newKey;

            var result = await _license.ValidateAsync(newKey);

            RefreshStatus();

            if (result.Valid)
            {
                var grace = result.IsGrace ? " (modo offline — grace period)" : "";
                MessageBox.Show(
                    $"Licença registrada com sucesso{grace}.",
                    "ConectaRevit — Licença",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                var reason = result.Reason switch
                {
                    "not_found"        => "Chave não encontrada.",
                    "suspended"        => "Licença suspensa. Entre em contato com o suporte.",
                    "expired"          => "Licença expirada. Renove para continuar.",
                    "machine_mismatch" => "Chave já ativada em outra máquina.",
                    "grace_expired"    => $"Grace period offline expirado ({LicensingConfig.GraceOfflineDays} dias sem validação). Conecte à Internet.",
                    "offline_no_cache" => "Sem conexão e sem cache local. Conecte à Internet para ativar.",
                    "signature_mismatch" => "Assinatura inválida — contate o suporte.",
                    _                  => $"Erro: {result.Reason ?? "desconhecido"}",
                };
                MessageBox.Show(
                    reason,
                    "ConectaRevit — Licença inválida",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text       = $"❌ Erro inesperado: {ex.Message}";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Crimson;
        }
        finally
        {
            BtnSave.IsEnabled = true;
            BtnSave.Content   = "Salvar e Validar";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
