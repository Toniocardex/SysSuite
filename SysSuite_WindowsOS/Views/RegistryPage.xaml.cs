using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.Views
{
    public sealed partial class RegistryPage : Page
    {
        private readonly RegistryService _svc;

        public RegistryPage()
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<RegistryService>();
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            btn.IsEnabled = false;
            BtnBackupRegistry.IsEnabled = false;
            BtnOpenRegedit.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            PbScan.IsIndeterminate = true;
            TxtCount.Text = "Scansione...";
            LvIssues.ItemsSource = null;
            try
            {
                var progress = new Progress<string>(p => TxtCount.Text = p.Length > 40 ? p.Substring(0, 40) + "..." : p);
                var issues = await _svc.ScanAsync(progress);
                LvIssues.ItemsSource = issues;
                TxtCount.Text = issues.Count == 0
                    ? "Nessun riferimento orfano trovato."
                    : issues.Count + " riferimenti orfani (solo informativo — nessuna azione necessaria)";
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
            finally
            {
                PbScan.IsIndeterminate = false;
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                BtnOpenRegedit.IsEnabled = true;
                BtnBackupRegistry.IsEnabled = true;
                btn.IsEnabled = true;
            }
        }

        private async void BtnBackup_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!AdminHelper.IsAdmin())
            {
                TxtCount.Text = "Backup registro richiede Admin. Usa 'Riavvia come Admin' in basso a destra.";
                return;
            }
            btn.IsEnabled = false;
            BtnScanRegistry.IsEnabled = false;
            BtnOpenRegedit.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try
            {
                var p = await _svc.BackupAsync("manuale");
                TxtCount.Text = "Backup: " + p;
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                BtnOpenRegedit.IsEnabled = true;
                BtnScanRegistry.IsEnabled = true;
                btn.IsEnabled = true;
            }
        }

        private void BtnOpenRegedit_Click(object s, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    UseShellExecute = true
                });
            }
            catch { TxtCount.Text = "Impossibile aprire l'Editor del Registro."; }
        }
    }
}
