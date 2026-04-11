using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.Views
{
    public sealed partial class RegistryPage : Page
    {
        private readonly RegistryService _svc = new();

        public RegistryPage()
        {
            InitializeComponent();
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            ((Button)sender).IsEnabled = false;
            PbScan.IsIndeterminate = true;
            TxtCount.Text = "Scansione...";
            LvIssues.ItemsSource = null;
            try
            {
                var progress = new Progress<string>(p => TxtCount.Text = p.Length > 40 ? p.Substring(0, 40) + "..." : p);
                var issues = await Task.Run(() => _svc.Scan(progress));
                LvIssues.ItemsSource = issues;
                TxtCount.Text = issues.Count == 0
                    ? "Nessun riferimento orfano trovato."
                    : issues.Count + " riferimenti orfani (solo informativo — nessuna azione necessaria)";
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
            finally { PbScan.IsIndeterminate = false; ((Button)sender).IsEnabled = true; }
        }

        private void BtnBackup_Click(object s, RoutedEventArgs e)
        {
            if (!AdminHelper.IsAdmin())
            {
                TxtCount.Text = "Backup registro richiede Admin. Usa 'Riavvia come Admin' in basso a destra.";
                return;
            }
            try { var p = _svc.Backup("manuale"); TxtCount.Text = "Backup: " + p; }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
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
