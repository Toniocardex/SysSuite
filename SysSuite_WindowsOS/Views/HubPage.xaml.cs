using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SysSuite.Core;
using SysSuite.Services;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SysSuite.Views
{
    public sealed partial class HubPage : Page
    {
        private readonly CleanupService       _clean    = new();
        private readonly RamOptimizerService _ramOpt   = new();
        private readonly BrowserService _browser = new();
        private readonly ProcessManager _pm      = new();

        public HubPage()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadInfo();
        }


        /// <summary>Naviga al modulo corrispondente alla card cliccata.</summary>
        private void ModuleCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
                MainWindow.Instance?.NavigateTo(tag);
        }
        private async void LoadInfo()
        {
            // ── Skeleton: mostra subito info persistenti mentre WMI carica ──
            try
            {
                var s = SettingsService.Load();
                if (s.LastBoostDate.HasValue)
                {
                    var ago = DateTime.Now - s.LastBoostDate.Value;
                    string agoStr = ago.TotalDays >= 1
                        ? ((int)ago.TotalDays) + "g fa"
                        : ago.Hours > 0 ? ago.Hours + "h fa" : "poco fa";
                    string mbStr = s.LastBoostMB >= 1 ? " — " + s.LastBoostMB + " MB liberati" : "";
                    TxtSub.Text = "Caricamento... (Ultimo Boost: " + agoStr + mbStr + ")";
                }
                else
                {
                    TxtSub.Text = "Caricamento informazioni sistema...";
                }
                // Segnaposto skeleton visibili
                TxtCPUName.Text = "Rilevamento in corso...";
                TxtGPUName.Text = "Rilevamento...";
            }
            catch { TxtSub.Text = "Caricamento..."; }

            // ── WMI su thread background — non blocca la UI ───────────
            SysSuite.Core.SystemInfo? info = null;
            try
            {
                info = await Task.Run(() => SysSuite.Core.SystemInfo.Collect());
            }
            catch (Exception ex)
            {
                TxtSub.Text = "Errore caricamento: " + ex.Message;
                return;
            }

            // ── Aggiorna la UI sul thread principale ──────────────────
            try
            {
                TxtSub.Text = "Sistema aggiornato — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm");

                // Aggiorna stato RAM Optimizer
                long freeMB  = RamOptimizerService.GetFreeRamMB();
                long totalMB = RamOptimizerService.GetTotalRamMB();
                long usedMB  = totalMB - freeMB;
                TxtRamStatus.Text = usedMB + " MB usati — " + freeMB + " MB liberi";

                TxtCPUName.Text   = info.CPUName;
                TxtCPUCores.Text  = info.CPUCores + " core / " + info.CPUThreads + " thread  " + info.CPUArch;
                TxtCPUFreq.Text   = info.CPUFreqStr.Length > 0 ? "Max " + info.CPUFreqStr : "";

                TxtRAMUsed.Text       = info.RAMUsedPct.ToString("0.#") + "%";
                TxtRAMDetail.Text     = info.RAMTotalGB + " GB fisici — " + info.RAMFreeGB + " GB liberi";
                TxtRAMCommercial.Text = "Taglia commerciale: " + info.RAMCommercialGB + " GB";
                UpdateDonut(ChartRAM, info.RAMUsedPct,
                    new SKColor(59, 158, 255), new SKColor(20, 24, 40));

                TxtDiskFree.Text  = info.DiskFreeGB.ToString("0.#") + " GB liberi";
                TxtDiskTotal.Text = info.DiskTotalGB + " GB totali — " + info.DiskUsedPct.ToString("0.#") + "% usato";
                UpdateDonut(ChartDisk, info.DiskUsedPct,
                    new SKColor(255, 181, 71), new SKColor(20, 24, 40));

                TxtOS.Text        = info.OSName;
                TxtOSVersion.Text = "Versione " + info.OSVersion + "  (Build " + info.OSBuild + ")";
                TxtUptime.Text    = info.Uptime.Days + "g " + info.Uptime.Hours + "h " + info.Uptime.Minutes + "m";
                TxtIP.Text        = string.IsNullOrEmpty(info.LocalIP) ? "—" : info.LocalIP;

                TxtGPUName.Text   = string.IsNullOrEmpty(info.GPUName) ? "—" : info.GPUName;
                TxtGPUVRAM.Text   = "VRAM: " + (string.IsNullOrEmpty(info.GPUVRAMStr) ? "n/d" : info.GPUVRAMStr);
            }
            catch (Exception ex)
            {
                TxtSub.Text = "Errore aggiornamento UI: " + ex.Message;
            }
        }

        // ── Donut helper ──────────────────────────────────────────────
        private static void UpdateDonut(
            LiveChartsCore.SkiaSharpView.WinUI.PieChart chart,
            double valuePct, SKColor color, SKColor background)
        {
            valuePct = Math.Clamp(valuePct, 0, 100);
            chart.Series = new ISeries[]
            {
                new PieSeries<double>
                {
                    Values = new double[] { valuePct }, InnerRadius = 28,
                    Fill = new SolidColorPaint(color), Stroke = null,
                    Pushout = 0, HoverPushout = 0, DataLabelsPaint = null
                },
                new PieSeries<double>
                {
                    Values = new double[] { 100 - valuePct }, InnerRadius = 28,
                    Fill = new SolidColorPaint(background), Stroke = null,
                    Pushout = 0, HoverPushout = 0, IsHoverable = false, DataLabelsPaint = null
                }
            };
        }

        // ── One-Click Boost ───────────────────────────────────────────
        private async void BtnBoost_Click(object sender, RoutedEventArgs e)
        {
            BtnBoost.IsEnabled = false;
            PbBoost.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            PbBoost.IsIndeterminate = true;
            TxtBoostResult.Text = "Pulizia in corso...";

            long freedBytes = 0; int killedProcs = 0;
            var boostOpt = SettingsService.Load();
            try
            {
                await Task.Run(() =>
                {
                    if (boostOpt.BoostCleanTemp)
                    { try { _clean.CleanTemp(); } catch { } }
                    if (boostOpt.BoostCleanThumbnails)
                    { try { _clean.CleanThumbnails(); } catch { } }
                    if (boostOpt.BoostCleanBrowserCache)
                    {
                        try
                        {
                            foreach (var b in _browser.DetectBrowsers().Where(b => b.Installed))
                            { freedBytes += _browser.GetCacheSize(b); _browser.CleanCache(b); }
                        }
                        catch { }
                    }
                    if (boostOpt.BoostKillNotResponding)
                    {
                        try
                        {
                            var stuck = _pm.GetProcesses()
                                .Where(p => !p.Responding && p.PID != 0
                                    && !string.IsNullOrEmpty(p.Path)
                                    && !p.Path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            foreach (var p in stuck) if (_pm.KillProcess(p.PID)) killedProcs++;
                        }
                        catch { }
                    }
                });

                PbBoost.IsIndeterminate = false;
                PbBoost.Value = 100;

                string freedStr = freedBytes >= 1_048_576
                    ? (freedBytes / 1_048_576) + " MB liberati"
                    : (freedBytes / 1024) + " KB liberati";
                string result = freedStr;
                if (killedProcs > 0) result += " · " + killedProcs + " proc. bloccati chiusi";

                TxtBoostResult.Text = "OK — " + result;
                SettingsService.Update(s =>
                {
                    s.LastBoostDate = DateTime.Now;
                    s.LastBoostMB   = freedBytes / 1_048_576;
                });
                ToastHelper.SendSuccess("SysSuite One — Boost completato", result);
                LoadInfo();
            }
            finally
            {
                PbBoost.IsIndeterminate = false;
                PbBoost.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                BtnBoost.IsEnabled = true;
            }
        }
        private async void BtnRamOpt_Click(object sender, RoutedEventArgs e)
        {
            // Avviso se la RAM libera è già abbondante (> 30%)
            long freeMB  = RamOptimizerService.GetFreeRamMB();
            long totalMB = RamOptimizerService.GetTotalRamMB();
            double freePct = totalMB > 0 ? freeMB * 100.0 / totalMB : 100;

            if (freePct > 30)
            {
                TxtRamResult.Text = "";
                TxtRamStatus.Text = freeMB + " MB liberi (" + freePct.ToString("0") + "%) — memoria già in buono stato";

                var dlg = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = "RAM già in buono stato",
                    Content = "La memoria disponibile è " + freePct.ToString("0") + "% (" + freeMB + " MB liberi).\n\n"
                            + "L'ottimizzazione forza Windows a spostare dati dalla RAM al disco, "
                            + "ma i programmi li richiederanno subito — l'effetto è temporaneo.\n\n"
                            + "Vuoi procedere comunque?",
                    PrimaryButtonText = "Procedi",
                    CloseButtonText = "Annulla",
                    XamlRoot = XamlRoot
                };
                if (await dlg.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    return;
            }

            BtnRamOpt.IsEnabled = false;
            BtnBoost.IsEnabled  = false;
            PbRamOpt.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            TxtRamResult.Text   = "Ottimizzazione in corso...";
            TxtRamStatus.Text   = "Svuoto i working set...";

            long freed = 0;
            try
            {
                await Task.Run(() =>
                {
                    _ramOpt.Log += (msg, type) =>
                        DispatcherQueue.TryEnqueue(() =>
                            TxtRamStatus.Text = msg.Length > 55 ? msg.Substring(0, 55) + "..." : msg);
                    freed = _ramOpt.Optimize();
                });

                if (freed > 0)
                {
                    TxtRamResult.Text = "+" + freed + " MB liberati (effetto temporaneo)";
                    ToastHelper.SendSuccess(
                        "SysSuite One — RAM Ottimizzata",
                        freed + " MB di RAM fisica recuperati.");
                }
                else
                {
                    TxtRamResult.Text = "RAM già ottimizzata";
                }

                await Task.Delay(500);
                LoadInfo();
            }
            finally
            {
                PbRamOpt.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                BtnRamOpt.IsEnabled = true;
                BtnBoost.IsEnabled  = true;
            }
        }

    }
}