using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SysSuite;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    /// <summary>
    /// Monitor live: campionamento CPU/RAM, grafico LiveCharts2, lista processi.
    /// Il polling è su <see cref="DispatcherTimer"/>; WMI/processi serializzati con <see cref="SemaphoreSlim"/> (1,1).
    /// Le proprietà UI si aggiornano solo dentro <c>_dispatcher.TryEnqueue</c> (mai GetForCurrentThread nei Task).
    /// </summary>
    public partial class MonitorViewModel : ObservableObject, IDisposable
    {
        private const int GraphPoints = 60;

        private readonly DispatcherQueue _dispatcher;
        private readonly ProcessManager _pm;
        private readonly SemaphoreSlim _sampleGate = new(1, 1);
        private readonly DispatcherTimer _timer;
        private readonly PerformanceCounter? _cpuCounter;

        private readonly ObservableCollection<float> _cpuValues = new();
        private readonly ObservableCollection<double> _ramValues = new();

        private readonly object _shutdownLock = new();
        private bool _disposed;

        private readonly ISeries[] _chartSeries;
        private readonly ICartesianAxis[] _chartXAxes;
        private readonly ICartesianAxis[] _chartYAxes;

        public IEnumerable<ISeries> ChartSeries => _chartSeries;
        public IEnumerable<ICartesianAxis> ChartXAxes => _chartXAxes;
        public IEnumerable<ICartesianAxis> ChartYAxes => _chartYAxes;

        public ObservableCollection<ProcessEntry> ProcessItems { get; } = new();

        public MonitorViewModel(DispatcherQueue dispatcher, ProcessManager pm)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _pm = pm;

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounter = null;
            }

            (_chartSeries, _chartXAxes, _chartYAxes) = BuildChartDefinitions(_cpuValues, _ramValues);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += OnTimerTick;
        }

        private static (ISeries[] series, ICartesianAxis[] xAxes, ICartesianAxis[] yAxes) BuildChartDefinitions(
            ObservableCollection<float> cpuValues, ObservableCollection<double> ramValues)
        {
            var accentGreen = new SKColor(0, 212, 160);
            var accentBlue = new SKColor(59, 158, 255);
            var gridColor = new SKColor(30, 42, 66, 60);
            var labelColor = new SKColor(61, 77, 102);

            var series = new ISeries[]
            {
                new LineSeries<float>
                {
                    Values = cpuValues,
                    Name = "CPU %",
                    Stroke = new SolidColorPaint(accentGreen, 2),
                    Fill = new LinearGradientPaint(
                        new[] { new SKColor(0, 212, 160, 60), new SKColor(0, 212, 160, 0) },
                        new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
                    GeometrySize = 0,
                    LineSmoothness = 0.5
                },
                new LineSeries<double>
                {
                    Values = ramValues,
                    Name = "RAM %",
                    Stroke = new SolidColorPaint(accentBlue, 2),
                    Fill = new LinearGradientPaint(
                        new[] { new SKColor(59, 158, 255, 60), new SKColor(59, 158, 255, 0) },
                        new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
                    GeometrySize = 0,
                    LineSmoothness = 0.5
                }
            };

            var xAxes = new ICartesianAxis[]
            {
                new Axis { IsVisible = false, ShowSeparatorLines = false }
            };

            var yAxes = new ICartesianAxis[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MaxLimit = 100,
                    LabelsPaint = new SolidColorPaint(labelColor),
                    SeparatorsPaint = new SolidColorPaint(gridColor),
                    TextSize = 10,
                    Labeler = v => v.ToString("0") + "%"
                }
            };

            return (series, xAxes, yAxes);
        }

        [ObservableProperty] private double _cpuUsage;
        [ObservableProperty] private double _ramUsage;
        [ObservableProperty] private string _liveCpuText = "";
        [ObservableProperty] private string _liveRamText = "";
        [ObservableProperty] private string _processCountText = "";
        [ObservableProperty] private string _searchFilter = "";
        [ObservableProperty] private string _sortColumn = "RamMB";
        [ObservableProperty] private bool _sortAscending;
        [ObservableProperty] private ProcessEntry? _selectedProcess;

        partial void OnSelectedProcessChanged(ProcessEntry? value) => KillSelectedCommand.NotifyCanExecuteChanged();

        [RelayCommand]
        private void StartMonitoring()
        {
            if (_disposed || _timer.IsEnabled)
                return;
            _timer.Start();
            _ = RunSampleAndProcessesAsync();
        }

        /// <summary>Ferma il timer, attende il campionamento in corso e rilascia le risorse (Unloaded).</summary>
        public void StopMonitoring() => ShutdownCore();

        private void OnTimerTick(object? sender, object? e) => _ = OnTimerTickAsync();

        [RelayCommand]
        private async Task RefreshAsync() => await RunSampleAndProcessesAsync().ConfigureAwait(true);

        [RelayCommand]
        private async Task SortByColumnAsync(string? column)
        {
            if (string.IsNullOrEmpty(column))
                return;
            if (SortColumn == column)
                SortAscending = !SortAscending;
            else
            {
                SortColumn = column;
                SortAscending = false;
            }

            await RunSampleAndProcessesAsync().ConfigureAwait(true);
        }

        [RelayCommand(CanExecute = nameof(CanKillSelected))]
        private async Task KillSelectedAsync()
        {
            if (SelectedProcess == null)
                return;

            int pid = SelectedProcess.PID;
            string name = SelectedProcess.Name;

            var xamlRoot = (MainWindow.Instance?.Content as FrameworkElement)?.XamlRoot;
            if (xamlRoot != null)
            {
                var confirm = new ContentDialog
                {
                    Title = "Termina processo",
                    Content = "Terminare il processo " + name + " (PID " + pid + ")?\n"
                              + "I dati non salvati andranno persi.",
                    PrimaryButtonText = "Termina",
                    CloseButtonText = "Annulla",
                    XamlRoot = xamlRoot
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }

            bool ok = _pm.KillProcess(pid);
            string msg = ok
                ? "Processo " + name + " terminato."
                : "Kill PID " + pid + " fallito (potrebbe richiedere Admin).";
            _dispatcher.TryEnqueue(() => ProcessCountText = msg);
            await RunSampleAndProcessesAsync().ConfigureAwait(true);
        }

        private bool CanKillSelected() => SelectedProcess != null;

        private async Task OnTimerTickAsync()
        {
            if (_disposed)
                return;

            float cpu = 0;
            try
            {
                if (_cpuCounter != null)
                    cpu = (float)Math.Round(_cpuCounter.NextValue(), 1);
            }
            catch
            {
                /* counter non disponibile */
            }

            if (!await _sampleGate.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                if (_disposed)
                    return;

                double ram = await Task.Run(QueryRamPercent).ConfigureAwait(false);
                string filterSnapshot = SearchFilter;
                string sortCol = SortColumn;
                bool sortAsc = SortAscending;
                var newData = await _pm.GetProcessesAsync(string.IsNullOrWhiteSpace(filterSnapshot) ? null : filterSnapshot)
                    .ConfigureAwait(false);
                var sorted = BuildSortedSnapshot(newData, sortCol, sortAsc, filterSnapshot);
                string countText = newData.Count + " processi";

                bool enqueued = _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed)
                        return;
                    try
                    {
                        ApplyLiveCpuRamSample(cpu, ram);
                        MergeProcessesList(sorted);
                        ProcessCountText = countText;
                    }
                    catch
                    {
                        /* evita crash UI su merge raro */
                    }
                });

                if (!enqueued)
                {
                    /* shutdown: coda non accetta lavoro */
                }
            }
            finally
            {
                try { _sampleGate.Release(); } catch { }
            }
        }

        private async Task RunSampleAndProcessesAsync()
        {
            if (_disposed)
                return;

            float cpu = 0;
            try
            {
                if (_cpuCounter != null)
                    cpu = (float)Math.Round(_cpuCounter.NextValue(), 1);
            }
            catch { }

            if (!await _sampleGate.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                if (_disposed)
                    return;

                double ram = await Task.Run(QueryRamPercent).ConfigureAwait(false);
                string filterSnapshot = SearchFilter;
                string sortCol = SortColumn;
                bool sortAsc = SortAscending;
                var newData = await _pm.GetProcessesAsync(string.IsNullOrWhiteSpace(filterSnapshot) ? null : filterSnapshot)
                    .ConfigureAwait(false);
                var sorted = BuildSortedSnapshot(newData, sortCol, sortAsc, filterSnapshot);
                string countText = newData.Count + " processi";

                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed)
                        return;
                    try
                    {
                        ApplyLiveCpuRamSample(cpu, ram);
                        MergeProcessesList(sorted);
                        ProcessCountText = countText;
                    }
                    catch { }
                });
            }
            finally
            {
                try { _sampleGate.Release(); } catch { }
            }
        }

        private void ApplyLiveCpuRamSample(float cpu, double ram)
        {
            CpuUsage = cpu;
            RamUsage = ram;

            _cpuValues.Add(cpu);
            if (_cpuValues.Count > GraphPoints)
                _cpuValues.RemoveAt(0);
            _ramValues.Add(ram);
            if (_ramValues.Count > GraphPoints)
                _ramValues.RemoveAt(0);

            LiveCpuText = "CPU " + cpu.ToString("0.#") + "%";
            LiveRamText = "RAM " + ram.ToString("0.#") + "%";
        }

        private static double QueryRamPercent()
        {
            try
            {
                using var q = new System.Management.ManagementObjectSearcher(
                    "SELECT FreePhysicalMemory,TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (System.Management.ManagementObject o in q.Get())
                {
                    double total = Convert.ToDouble(o["TotalVisibleMemorySize"]);
                    double free = Convert.ToDouble(o["FreePhysicalMemory"]);
                    if (total > 0)
                        return Math.Round((total - free) / total * 100, 1);
                }
            }
            catch
            {
                /* WMI non disponibile */
            }

            return 0;
        }

        private static List<ProcessEntry> BuildSortedSnapshot(List<ProcessEntry> newData, string sortCol, bool sortAsc,
            string filter)
        {
            IEnumerable<ProcessEntry> filtered = string.IsNullOrWhiteSpace(filter)
                ? newData
                : newData.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            return ApplySortStatic(filtered, sortCol, sortAsc).Take(150).ToList();
        }

        private static IEnumerable<ProcessEntry> ApplySortStatic(IEnumerable<ProcessEntry> procs, string sortCol,
            bool sortAsc) =>
            sortCol switch
            {
                "PID" => sortAsc ? procs.OrderBy(p => p.PID) : procs.OrderByDescending(p => p.PID),
                "Name" => sortAsc ? procs.OrderBy(p => p.Name) : procs.OrderByDescending(p => p.Name),
                "Threads" => sortAsc ? procs.OrderBy(p => p.Threads) : procs.OrderByDescending(p => p.Threads),
                "CpuPercent" => sortAsc ? procs.OrderBy(p => p.CpuPercent) : procs.OrderByDescending(p => p.CpuPercent),
                _ => sortAsc ? procs.OrderBy(p => p.RamMB) : procs.OrderByDescending(p => p.RamMB)
            };

        private void MergeProcessesList(List<ProcessEntry> sorted)
        {
            var sortedPids = new HashSet<int>(sorted.Select(p => p.PID));

            for (int i = ProcessItems.Count - 1; i >= 0; i--)
            {
                if (!sortedPids.Contains(ProcessItems[i].PID))
                    ProcessItems.RemoveAt(i);
            }

            var srcByPid = sorted.ToDictionary(p => p.PID);

            foreach (var live in ProcessItems)
            {
                if (srcByPid.TryGetValue(live.PID, out var snap))
                    live.CopyMetricsFrom(snap);
            }

            var livePids = new HashSet<int>(ProcessItems.Select(p => p.PID));
            foreach (var snap in sorted)
            {
                if (!livePids.Contains(snap.PID))
                    ProcessItems.Add(snap);
            }

            SyncOrderTo(ProcessItems, sorted);
        }

        private static void SyncOrderTo(ObservableCollection<ProcessEntry> items, List<ProcessEntry> sorted)
        {
            for (int t = 0; t < sorted.Count; t++)
            {
                int wantPid = sorted[t].PID;
                int found = -1;
                for (int i = t; i < items.Count; i++)
                {
                    if (items[i].PID != wantPid)
                        continue;
                    found = i;
                    break;
                }

                if (found < 0)
                    continue;
                if (found != t)
                    items.Move(found, t);
            }

            while (items.Count > sorted.Count)
                items.RemoveAt(items.Count - 1);
        }

        private void ShutdownCore()
        {
            lock (_shutdownLock)
            {
                if (_disposed)
                    return;

                _timer.Stop();
                _timer.Tick -= OnTimerTick;

                bool entered = false;
                try
                {
                    entered = _sampleGate.Wait(TimeSpan.FromSeconds(20));
                }
                catch (ObjectDisposedException) { }
                catch { }

                if (entered)
                {
                    try
                    {
                        _sampleGate.Release();
                    }
                    catch { }

                    try
                    {
                        _sampleGate.Dispose();
                    }
                    catch { }
                }

                try
                {
                    _cpuCounter?.Dispose();
                }
                catch { }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            ShutdownCore();
            GC.SuppressFinalize(this);
        }
    }
}
