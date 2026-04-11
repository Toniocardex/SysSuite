using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SysSuite.Models;
using SysSuite.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SysSuite.Views
{
    public sealed partial class MonitorPage : Page
    {
        private readonly ProcessManager _pm = new();
        private readonly PerformanceCounter? _cpuCounter;
        private readonly DispatcherQueueTimer _timer;

        private readonly ObservableCollection<float> _cpuValues = new();
        private readonly ObservableCollection<double> _ramValues = new();
        private const int GraphPoints = 60;

        private readonly ObservableCollection<ProcessEntry> _procItems = new();

        public MonitorPage()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch { }

            InitializeComponent();

            InitChart();

            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += async (_, _) =>
            {
                await CollectSampleAsync();
                await LoadProcessesMergedAsync();
            };

            Loaded += async (_, _) =>
            {
                LvProc.ItemsSource = _procItems;
                await LoadProcessesMergedAsync();
                _timer.Start();
            };
            Unloaded += (_, _) => { _timer.Stop(); _cpuCounter?.Dispose(); };
        }

        private void InitChart()
        {
            var accentGreen = new SKColor(0, 212, 160);
            var accentBlue = new SKColor(59, 158, 255);
            var gridColor = new SKColor(30, 42, 66, 60);
            var labelColor = new SKColor(61, 77, 102);

            ChartCpuRam.Series = new ISeries[]
            {
                new LineSeries<float>
                {
                    Values = _cpuValues,
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
                    Values = _ramValues,
                    Name = "RAM %",
                    Stroke = new SolidColorPaint(accentBlue, 2),
                    Fill = new LinearGradientPaint(
                        new[] { new SKColor(59, 158, 255, 60), new SKColor(59, 158, 255, 0) },
                        new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
                    GeometrySize = 0,
                    LineSmoothness = 0.5
                }
            };

            ChartCpuRam.XAxes = new Axis[]
            {
                new Axis { IsVisible = false, ShowSeparatorLines = false }
            };

            ChartCpuRam.YAxes = new Axis[]
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

            ChartCpuRam.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
            ChartCpuRam.AnimationsSpeed = TimeSpan.FromMilliseconds(200);
        }

        private bool _collecting;

        /// <summary>WMI RAM % — solo thread pool (non UI).</summary>
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
            catch { }
            return 0;
        }

        private void ApplyLiveCpuRamSample(float cpu, double ram)
        {
            _cpuValues.Add(cpu);
            if (_cpuValues.Count > GraphPoints) _cpuValues.RemoveAt(0);
            _ramValues.Add(ram);
            if (_ramValues.Count > GraphPoints) _ramValues.RemoveAt(0);

            TxtLiveCPU.Text = "CPU " + cpu.ToString("0.#") + "%";
            TxtLiveRAM.Text = "RAM " + ram.ToString("0.#") + "%";
        }

        private async Task CollectSampleAsync()
        {
            if (_collecting) return;
            _collecting = true;
            try
            {
                float cpu = _cpuCounter != null
                    ? (float)Math.Round(_cpuCounter.NextValue(), 1) : 0;

                double ram = await Task.Run(() => QueryRamPercent()).ConfigureAwait(false);

                if (DispatcherQueue.HasThreadAccess)
                {
                    ApplyLiveCpuRamSample(cpu, ram);
                    return;
                }

                var done = new TaskCompletionSource();
                bool enqueued = DispatcherQueue.TryEnqueue(() =>
                {
                    try { ApplyLiveCpuRamSample(cpu, ram); }
                    finally { done.TrySetResult(); }
                });
                if (!enqueued)
                    done.TrySetResult();
                await done.Task.ConfigureAwait(false);
            }
            catch { }
            finally { _collecting = false; }
        }

        private string _sortCol = "RamMB";
        private bool _sortAsc;
        private string _filter = "";

        private void BtnSortCol_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string col)
            {
                if (_sortCol == col) _sortAsc = !_sortAsc;
                else { _sortCol = col; _sortAsc = false; }
                _ = LoadProcessesMergedAsync();
            }
        }

        private void TxtFilter_Changed(object s, TextChangedEventArgs e)
        {
            _filter = TxtFilter.Text;
            _ = LoadProcessesMergedAsync();
        }

        private void BtnRefresh_Click(object s, RoutedEventArgs e) => _ = LoadProcessesMergedAsync();

        private bool _procLoadInFlight;

        private IEnumerable<ProcessEntry> ApplySort(IEnumerable<ProcessEntry> procs) =>
            _sortCol switch
            {
                "PID" => _sortAsc ? procs.OrderBy(p => p.PID) : procs.OrderByDescending(p => p.PID),
                "Name" => _sortAsc ? procs.OrderBy(p => p.Name) : procs.OrderByDescending(p => p.Name),
                "Threads" => _sortAsc ? procs.OrderBy(p => p.Threads) : procs.OrderByDescending(p => p.Threads),
                "CpuPercent" => _sortAsc ? procs.OrderBy(p => p.CpuPercent) : procs.OrderByDescending(p => p.CpuPercent),
                _ => _sortAsc ? procs.OrderBy(p => p.RamMB) : procs.OrderByDescending(p => p.RamMB)
            };

        private List<ProcessEntry> BuildSortedSnapshot(List<ProcessEntry> newData)
        {
            IEnumerable<ProcessEntry> filtered = string.IsNullOrWhiteSpace(_filter)
                ? newData
                : newData.Where(p => p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
            return ApplySort(filtered).Take(150).ToList();
        }

        /// <summary>Merge senza Clear: rimuove PID assenti, aggiorna INPC in-place, aggiunge nuovi, riallinea ordine.</summary>
        private void MergeProcessesList(List<ProcessEntry> sorted)
        {
            var sortedPids = new HashSet<int>(sorted.Select(p => p.PID));

            for (int i = _procItems.Count - 1; i >= 0; i--)
            {
                if (!sortedPids.Contains(_procItems[i].PID))
                    _procItems.RemoveAt(i);
            }

            var srcByPid = sorted.ToDictionary(p => p.PID);

            foreach (var live in _procItems)
            {
                if (srcByPid.TryGetValue(live.PID, out var snap))
                    live.CopyMetricsFrom(snap);
            }

            var livePids = new HashSet<int>(_procItems.Select(p => p.PID));
            foreach (var snap in sorted)
            {
                if (!livePids.Contains(snap.PID))
                    _procItems.Add(snap);
            }

            SyncOrderTo(_procItems, sorted);
        }

        private static void SyncOrderTo(ObservableCollection<ProcessEntry> items, List<ProcessEntry> sorted)
        {
            for (int t = 0; t < sorted.Count; t++)
            {
                int wantPid = sorted[t].PID;
                int found = -1;
                for (int i = t; i < items.Count; i++)
                {
                    if (items[i].PID == wantPid)
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0) continue;
                if (found != t)
                    items.Move(found, t);
            }

            while (items.Count > sorted.Count)
                items.RemoveAt(items.Count - 1);
        }

        private async Task LoadProcessesMergedAsync()
        {
            if (_procLoadInFlight) return;
            _procLoadInFlight = true;
            try
            {
                var newData = await _pm.GetProcessesAsync(string.IsNullOrWhiteSpace(_filter) ? null : _filter)
                    .ConfigureAwait(true);
                var sorted = BuildSortedSnapshot(newData);
                MergeProcessesList(sorted);
                TxtCount.Text = newData.Count + " processi";
            }
            catch { }
            finally { _procLoadInFlight = false; }
        }

        private async void BtnKill_Click(object s, RoutedEventArgs e)
        {
            if (LvProc.SelectedItem is not ProcessEntry entry) return;
            int pid = entry.PID;
            string name = entry.Name;

            var confirm = new ContentDialog
            {
                Title = "Termina processo",
                Content = "Terminare il processo " + name + " (PID " + pid + ")?\n"
                          + "I dati non salvati andranno persi.",
                PrimaryButtonText = "Termina",
                CloseButtonText = "Annulla",
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            bool ok = _pm.KillProcess(pid);
            TxtCount.Text = ok
                ? "Processo " + name + " terminato."
                : "Kill PID " + pid + " fallito (potrebbe richiedere Admin).";
            await LoadProcessesMergedAsync();
        }
    }
}
