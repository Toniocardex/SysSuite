using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
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
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    /// <summary>
    /// Dashboard: <see cref="SystemInfo.GatherAll"/> all'avvio, polling 1,5 s (CPU/RAM/GPU/rete/uptime),
    /// disco C: ogni 20 tick (~30 s con intervallo 1,5 s). GPU live: <see cref="GpuUsagePercentage"/> / <see cref="GpuUsedVram"/> via <see cref="GpuMonitorService"/> (DXGI).
    /// </summary>
    public partial class HubViewModel : ObservableObject, IDisposable
    {
        private const int DiskRefreshIntervalTicks = 20;

        private readonly DispatcherQueue _dispatcher;
        private readonly SystemInfo _systemInfo;
        private readonly CleanupService _cleanup;
        private readonly BrowserService _browser;
        private readonly ProcessManager _processManager;
        private readonly RamOptimizerService _ramOptimizer;
        private readonly GpuMonitorService _gpuMonitor;

        private readonly DispatcherTimer _dashTimer;
        private readonly PerformanceCounter? _cpuCounter;

        private long _netPrevBytesReceived;
        private long _netPrevBytesSent;
        private long _netPrevTickMs;
        private int _diskTickCounter;
        private int _dashboardTickBusy;
        private DateTime _bootWallUtc;
        private bool _dashboardFirstPaint = true;
        private bool _disposed;

        private static readonly SKColor DonutBg = new(20, 24, 40);
        private static readonly SKColor RamAccent = new(59, 158, 255);
        private static readonly SKColor DiskAccent = new(255, 181, 71);
        private static readonly SKColor GpuLiveAccent = new(167, 139, 250);

        public HubViewModel(
            DispatcherQueue dispatcher,
            SystemInfo systemInfo,
            CleanupService cleanup,
            BrowserService browser,
            ProcessManager processManager,
            RamOptimizerService ramOptimizer,
            GpuMonitorService gpuMonitor)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
            _cleanup = cleanup;
            _browser = browser;
            _processManager = processManager;
            _ramOptimizer = ramOptimizer;
            _gpuMonitor = gpuMonitor ?? throw new ArgumentNullException(nameof(gpuMonitor));

            RamDonutSeries = BuildDonutSeries(0, RamAccent, DonutBg);
            DiskDonutSeries = BuildDonutSeries(0, DiskAccent, DonutBg);
            GpuLiveDonutSeries = BuildDonutSeries(0, GpuLiveAccent, DonutBg);

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounter = null;
            }

            _dashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _dashTimer.Tick += OnDashboardTimerTick;
        }

        private void OnDashboardTimerTick(object? sender, object e)
        {
            _ = DashboardTimerWorkAsync();
        }

        [ObservableProperty] private string _subtitleText = "Caricamento informazioni sistema...";

        [ObservableProperty] private string _cpuLoadPercentText = "—";

        [ObservableProperty] private string _ramUsedPercentText = "—";
        [ObservableProperty] private string _ramDetailText = "—";
        [ObservableProperty] private string _ramCommercialText = "—";
        [ObservableProperty] private ISeries[] _ramDonutSeries = Array.Empty<ISeries>();

        [ObservableProperty] private string _diskFreeText = "—";
        [ObservableProperty] private string _diskTotalText = "—";
        [ObservableProperty] private ISeries[] _diskDonutSeries = Array.Empty<ISeries>();

        [ObservableProperty] private string _osNameText = "—";
        [ObservableProperty] private string _osVersionText = "—";
        [ObservableProperty] private string _liveUptimeText = "—";

        [ObservableProperty] private string _networkThroughputText = "—";

        [ObservableProperty] private string _snapshotLocalIpText = "—";

        [ObservableProperty] private HardwareSnapshotDisplay _hardwareSnapshot = HardwareSnapshotDisplay.Empty;

        /// <summary>Percentuale uso GPU (stringa es. <c>42%</c>) da <see cref="GpuMonitorService"/> DXGI.</summary>
        [ObservableProperty] private string _gpuUsagePercentage = "—";

        /// <summary>VRAM usata/totale formattata da DXGI (<see cref="GpuMonitorService"/>).</summary>
        [ObservableProperty] private string _gpuUsedVram = "—";
        [ObservableProperty] private ISeries[] _gpuLiveDonutSeries = Array.Empty<ISeries>();

        [ObservableProperty] private string _systemDiskModel = "N/D";

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
                PushLiveSampleToUi(updateDisk: true);
                if (_dashboardFirstPaint)
                {
                    MainContentVisibility = Visibility.Visible;
                    LoadingRingVisibility = Visibility.Collapsed;
                    _dashboardFirstPaint = false;
                }

                if (!_dashTimer.IsEnabled)
                    _dashTimer.Start();
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
                string processorText = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        string.IsNullOrWhiteSpace(info.CPUName) ? "—" : info.CPUName.Trim(),
                        info.CPUCores + " core / " + info.CPUThreads + " thread  " + info.CPUArch,
                        freqLine
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

        private async Task DashboardTimerWorkAsync()
        {
            if (Interlocked.Exchange(ref _dashboardTickBusy, 1) == 1)
                return;

            double cpu = 0;
            if (_cpuCounter != null)
            {
                try
                {
                    cpu = Math.Round(_cpuCounter.NextValue(), 1);
                }
                catch
                {
                    cpu = 0;
                }
            }

            RamOptimizerService.TryGetRamUsedPercent(out var ramPct, out var totalMb, out var freeMb);
            double ramTotalGb = totalMb / 1024.0;
            double ramFreeGb = freeMb / 1024.0;

            var gpuMetrics = _gpuMonitor.GetGpuMetrics();
            string gpuPctStr = gpuMetrics.UsagePercentage.ToString("0.#") + "%";
            string gpuVramStr = FormatBytes(gpuMetrics.UsedVramBytes) + " / " + FormatBytes(gpuMetrics.TotalVramBytes);

            string netLine = ComputeNetworkRatesText();
            var uptime = FormatUptime(DateTime.Now - _bootWallUtc);

            bool refreshDisk = false;
            var next = Interlocked.Increment(ref _diskTickCounter);
            if (next >= DiskRefreshIntervalTicks)
            {
                Interlocked.Exchange(ref _diskTickCounter, 0);
                refreshDisk = true;
            }

            if (refreshDisk)
            {
                try
                {
                    await Task.Run(() => _systemInfo.RefreshDiskVolumeOnly()).ConfigureAwait(false);
                }
                catch
                {
                    /* ignore */
                }
            }

            double diskUsedPct = _systemInfo.DiskUsedPct;
            string diskFree = _systemInfo.DiskFreeGB.ToString("0.#") + " GB liberi";
            string diskTotal = _systemInfo.DiskTotalGB + " GB totali — " + diskUsedPct.ToString("0.#") + "% usato";

            try
            {
                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed)
                        return;

                    CpuLoadPercentText = cpu.ToString("0.#") + "%";
                    RamUsedPercentText = ramPct.ToString("0.#") + "%";
                    RamDetailText = ramTotalGb.ToString("0.#") + " GB fisici — " + ramFreeGb.ToString("0.#") + " GB liberi";
                    RamDonutSeries = BuildDonutSeries(ramPct, RamAccent, DonutBg);

                    GpuUsagePercentage = gpuPctStr;
                    GpuUsedVram = gpuVramStr;
                    GpuLiveDonutSeries = BuildDonutSeries(gpuMetrics.UsagePercentage, GpuLiveAccent, DonutBg);

                    NetworkThroughputText = netLine;
                    LiveUptimeText = uptime;

                    if (refreshDisk)
                    {
                        DiskFreeText = diskFree;
                        DiskTotalText = diskTotal;
                        DiskDonutSeries = BuildDonutSeries(diskUsedPct, DiskAccent, DonutBg);
                    }
                });
            }
            finally
            {
                Interlocked.Exchange(ref _dashboardTickBusy, 0);
            }
        }

        /// <summary>Un campionamento immediato (disco incluso) prima del primo tick.</summary>
        private void PushLiveSampleToUi(bool updateDisk)
        {
            double cpu = 0;
            if (_cpuCounter != null)
            {
                try
                {
                    cpu = Math.Round(_cpuCounter.NextValue(), 1);
                }
                catch
                {
                    cpu = 0;
                }
            }

            RamOptimizerService.TryGetRamUsedPercent(out var ramPct, out var totalMb, out var freeMb);
            double ramTotalGb = totalMb / 1024.0;
            double ramFreeGb = freeMb / 1024.0;

            var gpuMetrics = _gpuMonitor.GetGpuMetrics();

            if (updateDisk)
            {
                DiskFreeText = _systemInfo.DiskFreeGB.ToString("0.#") + " GB liberi";
                DiskTotalText = _systemInfo.DiskTotalGB + " GB totali — " + _systemInfo.DiskUsedPct.ToString("0.#") + "% usato";
                DiskDonutSeries = BuildDonutSeries(_systemInfo.DiskUsedPct, DiskAccent, DonutBg);
            }

            CpuLoadPercentText = cpu.ToString("0.#") + "%";
            RamUsedPercentText = ramPct.ToString("0.#") + "%";
            RamDetailText = ramTotalGb.ToString("0.#") + " GB fisici — " + ramFreeGb.ToString("0.#") + " GB liberi";
            RamDonutSeries = BuildDonutSeries(ramPct, RamAccent, DonutBg);

            GpuUsagePercentage = gpuMetrics.UsagePercentage.ToString("0.#") + "%";
            GpuUsedVram = FormatBytes(gpuMetrics.UsedVramBytes) + " / " + FormatBytes(gpuMetrics.TotalVramBytes);
            GpuLiveDonutSeries = BuildDonutSeries(gpuMetrics.UsagePercentage, GpuLiveAccent, DonutBg);

            NetworkThroughputText = ComputeNetworkRatesText();
            LiveUptimeText = FormatUptime(DateTime.Now - _bootWallUtc);
        }

        private static string FormatUptime(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero)
                ts = TimeSpan.Zero;
            return ts.Days + "g " + ts.Hours + "h " + ts.Minutes + "m";
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

        private string ComputeNetworkRatesText()
        {
            try
            {
                long inB = 0, outB = 0;
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        continue;
                    IPInterfaceStatistics s = nic.GetIPStatistics();
                    inB += s.BytesReceived;
                    outB += s.BytesSent;
                }

                long now = Environment.TickCount64;
                if (_netPrevTickMs == 0)
                {
                    _netPrevBytesReceived = inB;
                    _netPrevBytesSent = outB;
                    _netPrevTickMs = now;
                    return "↑ —   ↓ —";
                }

                double dtMs = now - _netPrevTickMs;
                if (dtMs < 1)
                    dtMs = 1;
                double downBps = (inB - _netPrevBytesReceived) / (dtMs / 1000.0);
                double upBps = (outB - _netPrevBytesSent) / (dtMs / 1000.0);
                _netPrevBytesReceived = inB;
                _netPrevBytesSent = outB;
                _netPrevTickMs = now;

                return "↑ " + FormatDataRate(upBps) + "   ↓ " + FormatDataRate(downBps);
            }
            catch
            {
                return "↑ —   ↓ —";
            }
        }

        private static string FormatDataRate(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
                return bytesPerSecond.ToString("0") + " B/s";
            if (bytesPerSecond < 1024 * 1024)
                return (bytesPerSecond / 1024).ToString("0.#") + " KB/s";
            return (bytesPerSecond / (1024 * 1024)).ToString("0.##") + " MB/s";
        }

        private static ISeries[] BuildDonutSeries(double valuePct, SKColor color, SKColor background)
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

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _dashTimer.Stop();
            _dashTimer.Tick -= OnDashboardTimerTick;
            _cpuCounter?.Dispose();
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
