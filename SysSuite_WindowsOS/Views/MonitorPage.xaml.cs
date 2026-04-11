using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SysSuite.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.UI;

namespace SysSuite.Views
{
    public sealed partial class MonitorPage : Page
    {
        private readonly ProcessManager      _pm = new();
        private readonly PerformanceCounter? _cpuCounter;
        private readonly DispatcherQueueTimer _timer;

        // Buffer per il grafico — ObservableCollection aggiorna il chart automaticamente
        private readonly ObservableCollection<float>  _cpuValues = new();
        private readonly ObservableCollection<double> _ramValues = new();
        private const int GraphPoints = 60;

        public MonitorPage()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // prima lettura sempre 0 — scartiamo
            }
            catch { }

            InitializeComponent();

            // Configura il CartesianChart una volta sola
            InitChart();

            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += (_, _) =>
            {
                LoadProcesses();
                CollectSample();
            };

            Loaded   += (_, _) => { LoadProcesses(); _timer.Start(); };
            Unloaded += (_, _) => { _timer.Stop(); _cpuCounter?.Dispose(); };
        }

        // ── Grafico LiveCharts2 ─────────────────────────────────────────
        private void InitChart()
        {
            var accentGreen = new SKColor(0, 212, 160);   // #00D4A0
            var accentBlue  = new SKColor(59, 158, 255);  // #3B9EFF
            var gridColor   = new SKColor(30, 42, 66, 60);
            var labelColor  = new SKColor(61, 77, 102);   // #3D4D66

            ChartCpuRam.Series = new ISeries[]
            {
                new LineSeries<float>
                {
                    Values          = _cpuValues,
                    Name            = "CPU %",
                    Stroke          = new SolidColorPaint(accentGreen, 2),
                    Fill            = new LinearGradientPaint(
                        new[] { new SKColor(0, 212, 160, 60), new SKColor(0, 212, 160, 0) },
                        new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
                    GeometrySize    = 0,
                    LineSmoothness  = 0.5
                },
                new LineSeries<double>
                {
                    Values          = _ramValues,
                    Name            = "RAM %",
                    Stroke          = new SolidColorPaint(accentBlue, 2),
                    Fill            = new LinearGradientPaint(
                        new[] { new SKColor(59, 158, 255, 60), new SKColor(59, 158, 255, 0) },
                        new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
                    GeometrySize    = 0,
                    LineSmoothness  = 0.5
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
                    MinLimit         = 0,
                    MaxLimit         = 100,
                    LabelsPaint      = new SolidColorPaint(labelColor),
                    SeparatorsPaint  = new SolidColorPaint(gridColor),
                    TextSize         = 10,
                    Labeler          = v => v.ToString("0") + "%"
                }
            };

            ChartCpuRam.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
            ChartCpuRam.AnimationsSpeed = TimeSpan.FromMilliseconds(200);
        }

        private bool _collecting;

        private void CollectSample()
        {
            if (_collecting) return;
            _collecting = true;
            try
            {
                float cpu = _cpuCounter != null
                    ? (float)Math.Round(_cpuCounter.NextValue(), 1) : 0;

                double ram = 0;
                try
                {
                    using var q = new System.Management.ManagementObjectSearcher(
                        "SELECT FreePhysicalMemory,TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        double total = Convert.ToDouble(o["TotalVisibleMemorySize"]);
                        double free  = Convert.ToDouble(o["FreePhysicalMemory"]);
                        ram = total > 0 ? Math.Round((total - free) / total * 100, 1) : 0;
                    }
                }
                catch { }

                _cpuValues.Add(cpu);
                if (_cpuValues.Count > GraphPoints) _cpuValues.RemoveAt(0);
                _ramValues.Add(ram);
                if (_ramValues.Count > GraphPoints) _ramValues.RemoveAt(0);

                TxtLiveCPU.Text = "CPU " + cpu.ToString("0.#") + "%";
                TxtLiveRAM.Text = "RAM " + ram.ToString("0.#") + "%";
            }
            catch { }
            finally { _collecting = false; }
        }

        // ── Lista processi ──────────────────────────────────────────────

        private string _sortCol = "RamMB";
        private bool   _sortAsc = false;

        private void BtnSortCol_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string col)
            {
                if (_sortCol == col) _sortAsc = !_sortAsc;
                else { _sortCol = col; _sortAsc = false; }
                LoadProcesses();
            }
        }
        private string _filter = "";
        private void TxtFilter_Changed(object s, TextChangedEventArgs e)
        {
            _filter = TxtFilter.Text;
            LoadProcesses();
        }
        private void BtnRefresh_Click(object s, RoutedEventArgs e) => LoadProcesses();

        private bool _loadingProcs;

        private void LoadProcesses()
        {
            if (_loadingProcs) return;
            _loadingProcs = true;
            try
            {
                var procs = _pm.GetProcesses(string.IsNullOrWhiteSpace(_filter) ? null : _filter);
                var sorted = _sortCol switch
                {
                    "PID"        => _sortAsc ? procs.OrderBy(p => p.PID)         : procs.OrderByDescending(p => p.PID),
                    "Name"       => _sortAsc ? procs.OrderBy(p => p.Name)        : procs.OrderByDescending(p => p.Name),
                    "Threads"    => _sortAsc ? procs.OrderBy(p => p.Threads)     : procs.OrderByDescending(p => p.Threads),
                    "CpuPercent" => _sortAsc ? procs.OrderBy(p => p.CpuPercent)  : procs.OrderByDescending(p => p.CpuPercent),
                    _            => _sortAsc ? procs.OrderBy(p => p.RamMB)       : procs.OrderByDescending(p => p.RamMB)
                };
                LvProc.ItemsSource = sorted.Take(150).Select(p => new
                {
                    p.PID, p.Name, p.RamMB, p.Threads,
                    // CPU% con colore: verde <5%, ambra 5-30%, rosso >30%
                    CpuStr = p.CpuPercent > 0 ? p.CpuPercent.ToString("0.0") + "%" : "—",
                    CpuColor = new SolidColorBrush(
                        p.CpuPercent >= 30 ? Color.FromArgb(255, 255, 90,  90)   // rosso
                        : p.CpuPercent >= 5  ? Color.FromArgb(255, 255, 181, 71)  // ambra
                        :                      Color.FromArgb(255, 61,  77,  102)),// muted
                    RespondingText  = p.Responding ? "OK" : "Non risponde",
                    RespondingColor = p.Responding
                        ? new SolidColorBrush(Color.FromArgb(255, 52, 211, 153))
                        : new SolidColorBrush(Color.FromArgb(255, 255, 90, 90))
                }).ToList();
                TxtCount.Text = procs.Count + " processi";
            }
            catch { }
            finally { _loadingProcs = false; }
        }

        private async void BtnKill_Click(object s, RoutedEventArgs e)
        {
            if (LvProc.SelectedItem is not { } item) return;
            var pid  = (int)item.GetType().GetProperty("PID")!.GetValue(item)!;
            var name = item.GetType().GetProperty("Name")!.GetValue(item)?.ToString() ?? "";

            var confirm = new ContentDialog
            {
                Title             = "Termina processo",
                Content           = "Terminare il processo " + name + " (PID " + pid + ")?\n"
                                  + "I dati non salvati andranno persi.",
                PrimaryButtonText = "Termina",
                CloseButtonText   = "Annulla",
                XamlRoot          = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            bool ok = _pm.KillProcess(pid);
            TxtCount.Text = ok
                ? "Processo " + name + " terminato."
                : "Kill PID " + pid + " fallito (potrebbe richiedere Admin).";
            LoadProcesses();
        }
    }
}