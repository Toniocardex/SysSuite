using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SysSuite;
using SysSuite.Core;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    /// <summary>
    /// Dashboard Hub: aggrega <see cref="SystemInfo.GatherAll"/> sull'istanza iniettata e azioni rapide sui servizi condivisi.
    /// Nessun async nel costruttore: il caricamento è <see cref="LoadDashboardDataCommand"/>.
    /// </summary>
    public partial class HubViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly SystemInfo _systemInfo;
        private readonly CleanupService _cleanup;
        private readonly BrowserService _browser;
        private readonly ProcessManager _processManager;
        private readonly RamOptimizerService _ramOptimizer;

        private static readonly SKColor DonutBg = new(20, 24, 40);
        private static readonly SKColor RamAccent = new(59, 158, 255);
        private static readonly SKColor DiskAccent = new(255, 181, 71);

        /// <param name="dispatcher">Coda UI (solo da DI sul thread WinUI).</param>
        public HubViewModel(
            DispatcherQueue dispatcher,
            SystemInfo systemInfo,
            CleanupService cleanup,
            BrowserService browser,
            ProcessManager processManager,
            RamOptimizerService ramOptimizer)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
            _cleanup = cleanup;
            _browser = browser;
            _processManager = processManager;
            _ramOptimizer = ramOptimizer;

            RamDonutSeries = BuildDonutSeries(0, RamAccent, DonutBg);
            DiskDonutSeries = BuildDonutSeries(0, DiskAccent, DonutBg);
        }

        [ObservableProperty] private string _subtitleText = "Caricamento informazioni sistema...";

        [ObservableProperty] private string _cpuNameText = "—";
        [ObservableProperty] private string _cpuCoresText = "—";
        [ObservableProperty] private string _cpuFreqText = "—";

        [ObservableProperty] private string _ramUsedPercentText = "—";
        [ObservableProperty] private string _ramDetailText = "—";
        [ObservableProperty] private string _ramCommercialText = "—";
        [ObservableProperty] private IEnumerable<ISeries> _ramDonutSeries = Array.Empty<ISeries>();

        [ObservableProperty] private string _diskFreeText = "—";
        [ObservableProperty] private string _diskTotalText = "—";
        [ObservableProperty] private IEnumerable<ISeries> _diskDonutSeries = Array.Empty<ISeries>();

        [ObservableProperty] private string _osNameText = "—";
        [ObservableProperty] private string _osVersionText = "—";
        [ObservableProperty] private string _uptimeText = "—";
        [ObservableProperty] private string _localIpText = "—";

        [ObservableProperty] private string _gpuNameText = "—";
        [ObservableProperty] private string _gpuVramText = "—";

        [ObservableProperty] private string _ramDetails = "N/D";
        [ObservableProperty] private string _motherboardModel = "N/D";
        [ObservableProperty] private string _systemDiskModel = "N/D";
        [ObservableProperty] private string _displayDetails = "N/D";

        [ObservableProperty] private string _ramOptimizerStatusText = "Caricamento...";

        [ObservableProperty] private Visibility _boostProgressVisibility = Visibility.Collapsed;
        [ObservableProperty] private bool _boostProgressIndeterminate;
        [ObservableProperty] private double _boostProgressValue;
        [ObservableProperty] private string _boostResultText = "";

        [ObservableProperty] private Visibility _ramOptimizerProgressVisibility = Visibility.Collapsed;
        [ObservableProperty] private bool _ramOptimizerProgressIndeterminate = true;
        [ObservableProperty] private string _ramOptimizerResultText = "";

        [ObservableProperty] private bool _isOneClickBoostEnabled = true;
        [ObservableProperty] private bool _isRamOptimizerButtonEnabled = true;

        [RelayCommand]
        private async Task LoadDashboardDataAsync()
        {
            ApplySkeletonSubtitle();
            _dispatcher.TryEnqueue(ApplySkeletonCpuGpuLabels);

            try
            {
                await Task.Run(() => _systemInfo.GatherAll()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _dispatcher.TryEnqueue(() => SubtitleText = "Errore caricamento: " + ex.Message);
                return;
            }

            _dispatcher.TryEnqueue(() => ApplySystemInfo(_systemInfo));
        }

        private void ApplySkeletonSubtitle()
        {
            try
            {
                var s = SettingsService.Load();
                if (s.LastBoostDate.HasValue)
                {
                    var ago = DateTime.Now - s.LastBoostDate.Value;
                    string agoStr = ago.TotalDays >= 1
                        ? (int)ago.TotalDays + "g fa"
                        : ago.Hours > 0 ? ago.Hours + "h fa" : "poco fa";
                    string mbStr = s.LastBoostMB >= 1 ? " — " + s.LastBoostMB + " MB liberati" : "";
                    SubtitleText = "Caricamento... (Ultimo Boost: " + agoStr + mbStr + ")";
                }
                else
                    SubtitleText = "Caricamento informazioni sistema...";
            }
            catch
            {
                SubtitleText = "Caricamento...";
            }
        }

        private void ApplySkeletonCpuGpuLabels()
        {
            CpuNameText = "Rilevamento in corso...";
            GpuNameText = "Rilevamento...";
        }

        private void ApplySystemInfo(SystemInfo info)
        {
            try
            {
                SubtitleText = "Sistema aggiornato — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm");

                long freeMb = RamOptimizerService.GetFreeRamMB();
                long totalMb = RamOptimizerService.GetTotalRamMB();
                long usedMb = totalMb - freeMb;
                RamOptimizerStatusText = usedMb + " MB usati — " + freeMb + " MB liberi";

                CpuNameText = info.CPUName;
                CpuCoresText = info.CPUCores + " core / " + info.CPUThreads + " thread  " + info.CPUArch;
                CpuFreqText = info.CPUFreqStr.Length > 0 ? "Max " + info.CPUFreqStr : "";

                RamUsedPercentText = info.RAMUsedPct.ToString("0.#") + "%";
                RamDetailText = info.RAMTotalGB + " GB fisici — " + info.RAMFreeGB + " GB liberi";
                RamCommercialText = "Taglia commerciale: " + info.RAMCommercialGB + " GB";
                RamDonutSeries = BuildDonutSeries(info.RAMUsedPct, RamAccent, DonutBg);

                DiskFreeText = info.DiskFreeGB.ToString("0.#") + " GB liberi";
                DiskTotalText = info.DiskTotalGB + " GB totali — " + info.DiskUsedPct.ToString("0.#") + "% usato";
                DiskDonutSeries = BuildDonutSeries(info.DiskUsedPct, DiskAccent, DonutBg);

                OsNameText = info.OSName;
                OsVersionText = "Versione " + info.OSVersion + "  (Build " + info.OSBuild + ")";
                UptimeText = info.Uptime.Days + "g " + info.Uptime.Hours + "h " + info.Uptime.Minutes + "m";
                LocalIpText = string.IsNullOrEmpty(info.LocalIP) ? "—" : info.LocalIP;

                GpuNameText = string.IsNullOrEmpty(info.GPUName) ? "—" : info.GPUName;
                GpuVramText = "VRAM: " + (string.IsNullOrEmpty(info.GPUVRAMStr) ? "n/d" : info.GPUVRAMStr);

                RamDetails = string.IsNullOrWhiteSpace(info.RamHardwareDetails) ? "N/D" : info.RamHardwareDetails;
                MotherboardModel = string.IsNullOrWhiteSpace(info.MotherboardModel) ? "N/D" : info.MotherboardModel;
                SystemDiskModel = string.IsNullOrWhiteSpace(info.SystemDiskModel) ? "N/D" : info.SystemDiskModel;
                DisplayDetails = string.IsNullOrWhiteSpace(info.DisplayDetails) ? "N/D" : info.DisplayDetails;
            }
            catch (Exception ex)
            {
                SubtitleText = "Errore aggiornamento UI: " + ex.Message;
            }
        }

        private static IEnumerable<ISeries> BuildDonutSeries(double valuePct, SKColor color, SKColor background)
        {
            valuePct = Math.Clamp(valuePct, 0, 100);
            return new ISeries[]
            {
                new PieSeries<double>
                {
                    Values = new[] { valuePct },
                    InnerRadius = 28,
                    Fill = new SolidColorPaint(color),
                    Stroke = null,
                    Pushout = 0,
                    HoverPushout = 0,
                    DataLabelsPaint = null
                },
                new PieSeries<double>
                {
                    Values = new[] { 100 - valuePct },
                    InnerRadius = 28,
                    Fill = new SolidColorPaint(background),
                    Stroke = null,
                    Pushout = 0,
                    HoverPushout = 0,
                    IsHoverable = false,
                    DataLabelsPaint = null
                }
            };
        }

        [RelayCommand]
        private async Task RunOneClickBoostAsync()
        {
            IsOneClickBoostEnabled = false;
            BoostProgressVisibility = Visibility.Visible;
            BoostProgressIndeterminate = true;
            BoostResultText = "Pulizia in corso...";

            long freedBytes = 0;
            int killedProcs = 0;
            var boostOpt = SettingsService.Load();
            try
            {
                await Task.Run(() =>
                {
                    if (boostOpt.BoostCleanTemp)
                    {
                        try { _cleanup.CleanTemp(); }
                        catch { }
                    }

                    if (boostOpt.BoostCleanThumbnails)
                    {
                        try { _cleanup.CleanThumbnails(); }
                        catch { }
                    }

                    if (boostOpt.BoostCleanBrowserCache)
                    {
                        try
                        {
                            foreach (var b in _browser.DetectBrowsers().Where(b => b.Installed))
                            {
                                freedBytes += _browser.GetCacheSize(b);
                                _browser.CleanCache(b);
                            }
                        }
                        catch { }
                    }

                    if (boostOpt.BoostKillNotResponding)
                    {
                        try
                        {
                            var stuck = _processManager.GetProcesses()
                                .Where(p => !p.Responding && p.PID != 0
                                    && !string.IsNullOrEmpty(p.Path)
                                    && !p.Path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            foreach (var p in stuck)
                            {
                                if (_processManager.KillProcess(p.PID))
                                    killedProcs++;
                            }
                        }
                        catch { }
                    }
                }).ConfigureAwait(true);

                BoostProgressIndeterminate = false;
                BoostProgressValue = 100;

                string freedStr = freedBytes >= 1_048_576
                    ? freedBytes / 1_048_576 + " MB liberati"
                    : freedBytes / 1024 + " KB liberati";
                string result = freedStr;
                if (killedProcs > 0)
                    result += " · " + killedProcs + " proc. bloccati chiusi";

                BoostResultText = "OK — " + result;
                SettingsService.Update(s =>
                {
                    s.LastBoostDate = DateTime.Now;
                    s.LastBoostMB = freedBytes / 1_048_576;
                });
                ToastHelper.SendSuccess("SysSuite One — Boost completato", result);
                await LoadDashboardDataAsync().ConfigureAwait(true);
            }
            finally
            {
                BoostProgressIndeterminate = false;
                BoostProgressVisibility = Visibility.Collapsed;
                IsOneClickBoostEnabled = true;
            }
        }

        [RelayCommand]
        private async Task RunRamOptimizerAsync()
        {
            long freeMb = RamOptimizerService.GetFreeRamMB();
            long totalMb = RamOptimizerService.GetTotalRamMB();
            double freePct = totalMb > 0 ? freeMb * 100.0 / totalMb : 100;

            if (freePct > 30)
            {
                RamOptimizerResultText = "";
                RamOptimizerStatusText = freeMb + " MB liberi (" + freePct.ToString("0") + "%) — memoria già in buono stato";

                var xamlRoot = (MainWindow.Instance?.Content as FrameworkElement)?.XamlRoot;
                if (xamlRoot != null)
                {
                    var dlg = new ContentDialog
                    {
                        Title = "RAM già in buono stato",
                        Content = "La memoria disponibile è " + freePct.ToString("0") + "% (" + freeMb + " MB liberi).\n\n"
                                  + "L'ottimizzazione forza Windows a spostare dati dalla RAM al disco, "
                                  + "ma i programmi li richiederanno subito — l'effetto è temporaneo.\n\n"
                                  + "Vuoi procedere comunque?",
                        PrimaryButtonText = "Procedi",
                        CloseButtonText = "Annulla",
                        XamlRoot = xamlRoot
                    };
                    if (await dlg.ShowAsync() != ContentDialogResult.Primary)
                        return;
                }
            }

            IsRamOptimizerButtonEnabled = false;
            IsOneClickBoostEnabled = false;
            RamOptimizerProgressVisibility = Visibility.Visible;
            RamOptimizerProgressIndeterminate = true;
            RamOptimizerResultText = "Ottimizzazione in corso...";
            RamOptimizerStatusText = "Svuoto i working set...";

            void OnRamLog(string msg, string _)
            {
                _dispatcher.TryEnqueue(() =>
                    RamOptimizerStatusText = msg.Length > 55 ? msg[..55] + "..." : msg);
            }

            _ramOptimizer.Log += OnRamLog;
            long freed = 0;
            try
            {
                await Task.Run(() => { freed = _ramOptimizer.Optimize(); }).ConfigureAwait(true);

                if (freed > 0)
                {
                    RamOptimizerResultText = "+" + freed + " MB liberati (effetto temporaneo)";
                    ToastHelper.SendSuccess(
                        "SysSuite One — RAM Ottimizzata",
                        freed + " MB di RAM fisica recuperati.");
                }
                else
                    RamOptimizerResultText = "RAM già ottimizzata";

                await Task.Delay(500).ConfigureAwait(true);
                await LoadDashboardDataAsync().ConfigureAwait(true);
            }
            finally
            {
                _ramOptimizer.Log -= OnRamLog;
                RamOptimizerProgressVisibility = Visibility.Collapsed;
                IsRamOptimizerButtonEnabled = true;
                IsOneClickBoostEnabled = true;
            }
        }

        [RelayCommand]
        private void NavigateToModule(string? tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;
            MainWindow.Instance?.NavigateTo(tag);
        }
    }
}
