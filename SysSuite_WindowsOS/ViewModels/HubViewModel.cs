using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Windows.UI;
using SysSuite;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    /// <summary>
    /// Dashboard: <see cref="SystemInfo.GatherAll"/> all'avvio. Donut disco (LiveCharts) aggiornato ogni 30 s; GPU motori DXGI+perf ogni 10 s.
    /// </summary>
    public partial class HubViewModel : ObservableObject, IDisposable
    {
        private static readonly TimeSpan GpuRefreshInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DiskRefreshInterval = TimeSpan.FromSeconds(30);

        private readonly DispatcherQueue _dispatcher;
        private readonly SystemInfo _systemInfo;
        private readonly CleanupService _cleanup;
        private readonly BrowserService _browser;
        private readonly ProcessManager _processManager;
        private readonly RamOptimizerService _ramOptimizer;
        private readonly GpuMonitorService _gpuMonitor;
        private readonly StorageHealthService _storageHealth;

        private readonly DispatcherTimer _gpuSlowTimer;
        private readonly DispatcherTimer _diskRefreshTimer;
        private int _gpuTickBusy;
        private int _diskTickBusy;
        private DateTime _bootWallUtc;
        private bool _dashboardFirstPaint = true;
        private bool _disposed;

        private static readonly SKColor DonutBg = new(20, 24, 40);
        private static readonly SKColor DiskAccent = new(255, 181, 71);

        /// <summary>Pennelli brand GPU (singleton): nessuna nuova allocazione a ogni tick DXGI.</summary>
        private static readonly SolidColorBrush GpuBrandBrushNvidia = new(Color.FromArgb(255, 0x76, 0xB9, 0x00));
        private static readonly SolidColorBrush GpuBrandBrushAmd = new(Color.FromArgb(255, 0xED, 0x1C, 0x24));
        private static readonly SolidColorBrush GpuBrandBrushIntel = new(Color.FromArgb(255, 0x00, 0x71, 0xC5));
        private static readonly SolidColorBrush GpuBrandBrushDefault = new(Color.FromArgb(255, 0x3B, 0x9E, 0xFF));

        private GpuBrandKind _gpuBrandKind = GpuBrandKind.Default;

        public HubViewModel(
            DispatcherQueue dispatcher,
            SystemInfo systemInfo,
            CleanupService cleanup,
            BrowserService browser,
            ProcessManager processManager,
            RamOptimizerService ramOptimizer,
            GpuMonitorService gpuMonitor,
            StorageHealthService storageHealth)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
            _cleanup = cleanup;
            _browser = browser;
            _processManager = processManager;
            _ramOptimizer = ramOptimizer;
            _gpuMonitor = gpuMonitor ?? throw new ArgumentNullException(nameof(gpuMonitor));
            _storageHealth = storageHealth ?? throw new ArgumentNullException(nameof(storageHealth));

            _gpuSlowTimer = new DispatcherTimer { Interval = GpuRefreshInterval };
            _gpuSlowTimer.Tick += OnGpuSlowTimerTick;

            _diskRefreshTimer = new DispatcherTimer { Interval = DiskRefreshInterval };
            _diskRefreshTimer.Tick += OnDiskRefreshTimerTick;

            DiskDonutSeries = BuildDiskDonutSeries(0);
        }

        private void OnGpuSlowTimerTick(object? sender, object e)
        {
            ApplyGpuSlowSampleToUi();
        }

        private void OnDiskRefreshTimerTick(object? sender, object e)
        {
            _ = RefreshDiskVolumeUiAsync();
        }

        [ObservableProperty] private string _subtitleText = "Caricamento informazioni sistema...";

        /// <summary>Righe CPU per card (testo strutturato, senza muro di newline).</summary>
        [ObservableProperty] private string _dashboardCpuName = "—";
        [ObservableProperty] private string _dashboardCpuCoresLine = "";
        [ObservableProperty] private string _dashboardCpuFreqLine = "";

        [ObservableProperty] private string _ramUsedPercentText = "—";
        [ObservableProperty] private string _ramDetailText = "—";
        [ObservableProperty] private string _ramCommercialText = "—";
        /// <summary>0–100 per ProgressBar RAM (snapshot al load / dopo refresh esplicito).</summary>
        [ObservableProperty] private double _ramUsedPercentValue;

        [ObservableProperty] private string _diskFreeText = "—";
        [ObservableProperty] private string _diskTotalText = "—";
        /// <summary>0–100 per ProgressBar disco (snapshot).</summary>
        [ObservableProperty] private double _diskUsedPercentValue;

        /// <summary>Donut LiveCharts solo per card Archiviazione (aggiornato con il timer 30 s).</summary>
        [ObservableProperty] private ISeries[] _diskDonutSeries = Array.Empty<ISeries>();

        [ObservableProperty] private string _osNameText = "—";
        [ObservableProperty] private string _osVersionText = "—";
        [ObservableProperty] private string _liveUptimeText = "—";

        [ObservableProperty] private string _snapshotLocalIpText = "—";

        [ObservableProperty] private HardwareSnapshotDisplay _hardwareSnapshot = HardwareSnapshotDisplay.Empty;

        /// <summary>Carico motori GPU (contatori Windows) o fallback allocazione VRAM DXGI — vedi <see cref="GpuMonitorService"/>.</summary>
        [ObservableProperty] private string _gpuUsagePercentage = "—";

        /// <summary>VRAM usata/totale formattata da DXGI (<see cref="GpuMonitorService"/>).</summary>
        [ObservableProperty] private string _gpuUsedVram = "—";
        /// <summary>0–100: motori se disponibili, altrimenti percentuale VRAM DXGI.</summary>
        [ObservableProperty] private double _gpuUsagePercentValue;

        /// <summary>Colore brand GPU (icona + barra VRAM); aggiornato solo se il vendor DXGI cambia.</summary>
        [ObservableProperty] private SolidColorBrush _gpuBrandBrush = GpuBrandBrushDefault;

        [ObservableProperty] private string _systemDiskModel = "N/D";

        /// <summary>Temperatura disco (lettura nativa), aggiornata col timer disco 30 s.</summary>
        [ObservableProperty] private string _diskTemperature = "—";

        /// <summary>Salute / TBW host (NVMe), aggiornata col timer disco 30 s.</summary>
        [ObservableProperty] private string _diskHealth = "—";

        [ObservableProperty] private string _ramOptimizerStatusText = "Caricamento...";

        [ObservableProperty] private Visibility _loadingRingVisibility = Visibility.Visible;

        [ObservableProperty] private Visibility _mainContentVisibility = Visibility.Collapsed;

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

            try
            {
                await Task.Run(() => _systemInfo.GatherAll()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    SubtitleText = "Errore caricamento: " + ex.Message;
                    LoadingRingVisibility = Visibility.Collapsed;
                });
                return;
            }

            _dispatcher.TryEnqueue(() =>
            {
                ApplySystemInfo(_systemInfo);
                ApplyRamDiskGpuSnapshots();
                if (_dashboardFirstPaint)
                {
                    MainContentVisibility = Visibility.Visible;
                    LoadingRingVisibility = Visibility.Collapsed;
                    _dashboardFirstPaint = false;
                }

                if (!_gpuSlowTimer.IsEnabled)
                    _gpuSlowTimer.Start();
                if (!_diskRefreshTimer.IsEnabled)
                    _diskRefreshTimer.Start();
            });
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

        private void ApplySystemInfo(SystemInfo info)
        {
            try
            {
                SubtitleText = "Sistema aggiornato — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm");

                long freeMb = RamOptimizerService.GetFreeRamMB();
                long totalMb = RamOptimizerService.GetTotalRamMB();
                long usedMb = totalMb - freeMb;
                RamOptimizerStatusText = usedMb + " MB usati — " + freeMb + " MB liberi";

                string freqLine = info.CPUFreqStr.Length > 0 ? "Max " + info.CPUFreqStr : "";
                DashboardCpuName = string.IsNullOrWhiteSpace(info.CPUName) ? "—" : info.CPUName.Trim();
                DashboardCpuCoresLine = info.CPUCores + " core / " + info.CPUThreads + " thread · " + info.CPUArch;
                DashboardCpuFreqLine = string.IsNullOrWhiteSpace(freqLine) ? "" : freqLine;

                string processorText = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        DashboardCpuName,
                        DashboardCpuCoresLine,
                        DashboardCpuFreqLine
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                string gpuModelText = string.IsNullOrWhiteSpace(info.GPUName) ? "—" : info.GPUName.Trim();

                RamCommercialText = "Taglia commerciale: " + info.RAMCommercialGB + " GB";

                string ramDetails = string.IsNullOrWhiteSpace(info.RamHardwareDetails) ? "N/D" : info.RamHardwareDetails;
                string motherboardModel = string.IsNullOrWhiteSpace(info.MotherboardModel) ? "N/D" : info.MotherboardModel;
                SystemDiskModel = string.IsNullOrWhiteSpace(info.SystemDiskModel) ? "N/D" : info.SystemDiskModel;
                string displayDetails = string.IsNullOrWhiteSpace(info.DisplayDetails) ? "N/D" : info.DisplayDetails;

                HardwareSnapshot = new HardwareSnapshotDisplay(
                    processorText,
                    gpuModelText,
                    ramDetails,
                    motherboardModel,
                    displayDetails);

                OsNameText = info.OSName;
                OsVersionText = "Versione " + info.OSVersion + "  (Build " + info.OSBuild + ")";
                _bootWallUtc = DateTime.Now - info.Uptime;
                LiveUptimeText = FormatUptime(DateTime.Now - _bootWallUtc);

                SnapshotLocalIpText = string.IsNullOrEmpty(info.LocalIP) ? "—" : info.LocalIP;
            }
            catch (Exception ex)
            {
                SubtitleText = "Errore aggiornamento UI: " + ex.Message;
            }
        }

        /// <summary>Snapshot RAM/disco (nessun polling) + primo campione GPU; chiamare sul thread UI dopo <see cref="ApplySystemInfo"/>.</summary>
        private void ApplyRamDiskGpuSnapshots()
        {
            RamOptimizerService.TryGetRamUsedPercent(out var ramPct, out var totalMb, out var freeMb);
            double ramTotalGb = totalMb / 1024.0;
            double ramFreeGb = freeMb / 1024.0;
            RamUsedPercentText = ramPct.ToString("0.#") + "%";
            RamDetailText = ramTotalGb.ToString("0.#") + " GB fisici — " + ramFreeGb.ToString("0.#") + " GB liberi";
            RamUsedPercentValue = ramPct;

            double diskUsedPct = _systemInfo.DiskUsedPct;
            DiskFreeText = _systemInfo.DiskFreeGB.ToString("0.#") + " GB liberi";
            DiskTotalText = _systemInfo.DiskTotalGB + " GB totali — " + diskUsedPct.ToString("0.#") + "% usato";
            DiskUsedPercentValue = diskUsedPct;
            DiskDonutSeries = BuildDiskDonutSeries(diskUsedPct);

            ApplyGpuMetricsToProperties(_gpuMonitor.GetGpuMetrics());
        }

        private async Task RefreshDiskVolumeUiAsync()
        {
            if (Interlocked.Exchange(ref _diskTickBusy, 1) == 1)
                return;

            try
            {
                (int? tempC, byte? pctUsed, double? hostWriteTb) metrics = (null, null, null);
                try
                {
                    await Task.Run(() =>
                    {
                        _systemInfo.RefreshDiskVolumeOnly();
                        metrics = _storageHealth.TryReadPrimaryDriveMetrics();
                    }).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                double diskUsedPct = _systemInfo.DiskUsedPct;
                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed)
                        return;
                    DiskFreeText = _systemInfo.DiskFreeGB.ToString("0.#") + " GB liberi";
                    DiskTotalText = _systemInfo.DiskTotalGB + " GB totali — " + diskUsedPct.ToString("0.#") + "% usato";
                    DiskUsedPercentValue = diskUsedPct;
                    DiskDonutSeries = BuildDiskDonutSeries(diskUsedPct);
                    DiskTemperature = FormatDiskTemperature(metrics.tempC);
                    DiskHealth = FormatDiskHealth(metrics.pctUsed, metrics.hostWriteTb);
                });
            }
            finally
            {
                Interlocked.Exchange(ref _diskTickBusy, 0);
            }
        }

        private static ISeries[] BuildDiskDonutSeries(double valuePct)
        {
            valuePct = Math.Clamp(valuePct, 0, 100);
            return new ISeries[]
            {
                new PieSeries<double>
                {
                    Values = new[] { valuePct },
                    InnerRadius = 32,
                    Fill = new SolidColorPaint(DiskAccent),
                    Stroke = null,
                    Pushout = 0,
                    HoverPushout = 0,
                    DataLabelsPaint = null
                },
                new PieSeries<double>
                {
                    Values = new[] { 100 - valuePct },
                    InnerRadius = 32,
                    Fill = new SolidColorPaint(DonutBg),
                    Stroke = null,
                    Pushout = 0,
                    HoverPushout = 0,
                    IsHoverable = false,
                    DataLabelsPaint = null
                }
            };
        }

        private void ApplyGpuSlowSampleToUi()
        {
            if (Interlocked.Exchange(ref _gpuTickBusy, 1) == 1)
                return;

            try
            {
                if (_disposed)
                    return;

                ApplyGpuMetricsToProperties(_gpuMonitor.GetGpuMetrics());
            }
            finally
            {
                Interlocked.Exchange(ref _gpuTickBusy, 0);
            }
        }

        private void ApplyGpuMetricsToProperties(GpuMetrics gpuMetrics)
        {
            if (gpuMetrics.EngineUtilizationPercent is { } eng)
            {
                GpuUsagePercentage = eng.ToString("0.#") + "%";
                GpuUsagePercentValue = eng;
            }
            else
            {
                GpuUsagePercentage = gpuMetrics.VramUsagePercent.ToString("0.#") + "% (solo VRAM)";
                GpuUsagePercentValue = gpuMetrics.VramUsagePercent;
            }

            GpuUsedVram = FormatBytes(gpuMetrics.UsedVramBytes) + " / " + FormatBytes(gpuMetrics.TotalVramBytes);
            TrySetGpuBrandBrushFromAdapterName(gpuMetrics.Name);
        }

        private void TrySetGpuBrandBrushFromAdapterName(string? adapterName)
        {
            var kind = ClassifyGpuBrand(adapterName);
            if (kind == _gpuBrandKind)
                return;
            _gpuBrandKind = kind;
            GpuBrandBrush = kind switch
            {
                GpuBrandKind.Nvidia => GpuBrandBrushNvidia,
                GpuBrandKind.Amd => GpuBrandBrushAmd,
                GpuBrandKind.Intel => GpuBrandBrushIntel,
                _ => GpuBrandBrushDefault,
            };
        }

        private static GpuBrandKind ClassifyGpuBrand(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return GpuBrandKind.Default;
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                return GpuBrandKind.Nvidia;
            if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                return GpuBrandKind.Amd;
            if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                return GpuBrandKind.Intel;
            return GpuBrandKind.Default;
        }

        private enum GpuBrandKind
        {
            Nvidia,
            Amd,
            Intel,
            Default,
        }

        private static string FormatUptime(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero)
                ts = TimeSpan.Zero;
            return ts.Days + "g " + ts.Hours + "h " + ts.Minutes + "m";
        }

        private static string FormatDiskTemperature(int? celsius) =>
            celsius.HasValue ? celsius.Value + " °C" : "—";

        private static string FormatDiskHealth(byte? percentageUsed, double? hostWriteTb)
        {
            if (!percentageUsed.HasValue && !hostWriteTb.HasValue)
                return "—";
            string part = percentageUsed.HasValue
                ? "Residuo stimato " + Math.Clamp(100 - percentageUsed.Value, 0, 100) + "%"
                : "Salute: n/d";
            if (hostWriteTb.HasValue)
                part += " · ~" + hostWriteTb.Value.ToString("0.#") + " TB host";
            return part;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                bytes = 0;
            double gb = bytes / 1073741824.0;
            if (gb >= 0.1)
                return gb.ToString("0.#") + " GB";
            double mb = bytes / 1048576.0;
            return mb.ToString("0.#") + " MB";
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _gpuSlowTimer.Stop();
            _gpuSlowTimer.Tick -= OnGpuSlowTimerTick;
            _diskRefreshTimer.Stop();
            _diskRefreshTimer.Tick -= OnDiskRefreshTimerTick;
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
