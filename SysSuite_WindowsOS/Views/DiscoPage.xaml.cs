using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Services;
using SysSuite.Models;

namespace SysSuite.Views
{
    public sealed partial class DiscoPage : Page
    {
        private readonly DiskAnalyzer  _analyzer = new();
        private readonly CleanupService _cleanup  = new();
        public DiscoPage()
        {
            InitializeComponent();
            _cleanup.Log += (m, t) => System.Diagnostics.Debug.WriteLine("[" + t + "] " + m);
            LvItems.DoubleTapped += LvItems_DoubleTapped;
        }

        private void LvItems_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (LvItems.SelectedItem is not { } item) return;
            var path = item.GetType().GetProperty("Path")?.GetValue(item)?.ToString() ?? "";
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                // Apre la cartella contenitore nel File Explorer
                string folder = System.IO.File.Exists(path)
                    ? System.IO.Path.GetDirectoryName(path) ?? path
                    : path;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "explorer.exe",
                    Arguments       = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void SetScanBusy(bool busy)
        {
            BtnHeavyFolders.IsEnabled = !busy;
            BtnLargeFiles.IsEnabled   = !busy;
            BtnSmart.IsEnabled        = !busy;
            BtnDuplicates.IsEnabled   = !busy;
            BusyRing.Visibility       = busy ? Visibility.Visible : Visibility.Collapsed;
            BusyRing.IsActive         = busy;
            PbScan.IsIndeterminate    = busy;
        }

        private async void BtnFolders_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            btn.IsEnabled = false;
            SetScanBusy(true);
            TxtStatus.Text = "Analisi in corso...";
            LvItems.ItemsSource = null;
            var progress = new Progress<string>(p => TxtStatus.Text = p.Length > 40 ? p[..40]+"..." : p);
            try
            {
                var result = await _analyzer.GetHeavyFoldersAsync(@"C:\", 2, progress);
                LvItems.ItemsSource = result.Select(d => new { Size = d.SizeFormatted, Path = d.Path }).ToList();
                TxtStatus.Text = $"{result.Count} cartelle";
            }
            catch (Exception ex) { TxtStatus.Text = $"Errore: {ex.Message}"; }
            finally
            {
                SetScanBusy(false);
                btn.IsEnabled = true;
            }
        }

        private async void BtnLargeFiles_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            btn.IsEnabled = false;
            SetScanBusy(true);
            TxtScanType.Text = "FILE GRANDI (> 100 MB) — C:";
            TxtStatus.Text = "Ricerca file grandi...";
            LvItems.ItemsSource = null;
            var progress = new Progress<string>(p => TxtStatus.Text = p.Length > 40 ? p[..40]+"..." : p);
            try
            {
                var result = await _analyzer.GetLargeFilesAsync(@"C:\", 100*1024*1024, progress);
                LvItems.ItemsSource = result.Select(d => new { Size = d.SizeFormatted, Path = System.IO.Path.Combine(d.Path, d.Name) }).ToList();
                TxtStatus.Text = $"{result.Count} file";
            }
            catch (Exception ex) { TxtStatus.Text = $"Errore: {ex.Message}"; }
            finally
            {
                SetScanBusy(false);
                btn.IsEnabled = true;
            }
        }

        private async void BtnSmart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            btn.IsEnabled = false;
            SetScanBusy(true);
            TxtScanType.Text = "DATI S.M.A.R.T. — TUTTI I DISCHI";
            TxtStatus.Text = "Lettura SMART...";
            LvItems.ItemsSource = null;
            try
            {
                var result = await _analyzer.GetSmartDataAsync();
                LvItems.ItemsSource = result.Select(d => new
                {
                    Size = d.FailPredicted ? "GUASTO" : "OK",
                    Path = $"{d.DiskModel}  S/N:{d.SerialNumber}  {d.Interface}  {Math.Round(d.SizeBytes/1_073_741_824.0,1)} GB"
                }).ToList();
                TxtStatus.Text = $"{result.Count} dischi";
            }
            catch (Exception ex) { TxtStatus.Text = $"Errore: {ex.Message}"; }
            finally
            {
                SetScanBusy(false);
                btn.IsEnabled = true;
            }
        }

        private async void BtnDuplicates_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            btn.IsEnabled = false;
            SetScanBusy(true);
            TxtScanType.Text = "FILE DUPLICATI — C:\\Users";
            TxtStatus.Text = "Ricerca duplicati (puo richiedere alcuni minuti)...";
            LvItems.ItemsSource = null;
            var progress = new Progress<string>(p =>
                TxtStatus.Text = p.Length > 50 ? p.Substring(0, 50) + "..." : p);
            try
            {
                var result = await _cleanup.FindDuplicatesAsync(@"C:\Users", progress);
                long totalWasted = result.Sum(g => g.WastedBytes);
                LvItems.ItemsSource = result.Select(g => new
                {
                    Size = SysSuite.Models.DiskEntry.FormatSize(g.WastedBytes) + " sprecati",
                    Path = g.FileName + "  (" + g.Files.Count + " copie)"
                }).ToList();
                TxtStatus.Text = result.Count + " gruppi — " +
                    SysSuite.Models.DiskEntry.FormatSize(totalWasted) + " recuperabili";
            }
            catch (Exception ex) { TxtStatus.Text = "Errore: " + ex.Message; }
            finally
            {
                SetScanBusy(false);
                btn.IsEnabled = true;
            }
        }

    }
}
