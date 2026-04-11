using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using SysSuite.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Views;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SysSuite
{
    public sealed partial class MainWindow : Window
    {
        /// <summary>Riferimento statico per navigazione da altre pagine.</summary>
        public static MainWindow? Instance { get; private set; }
        private readonly SystemInfo _sysInfo;
        private readonly DispatcherQueueTimer _timer;
        private PerformanceCounter? _cpuCounter;
        private ListViewItem? _lastNavItem;

        // Stato notifiche proattive — evita spam ripetuto
        private bool   _notifiedDiskFull    = false;
        private bool   _notifiedDrivers     = false;
        private int    _highCpuStreak       = 0;   // tick consecutivi con CPU > soglia
        private bool   _notifiedHighCpu     = false;
        private int    _highRamStreak       = 0;
        private bool   _notifiedHighRam     = false;
        private DateTime _lastDriverCheck   = DateTime.MinValue;
        private bool _suppressNavSelection;

        private static readonly Dictionary<string, Type> NavPagesMap = new()
        {
            ["Hub"]        = typeof(HubPage),
            ["MainSuite"]  = typeof(MainSuitePage),
            ["Monitor"]    = typeof(MonitorPage),
            ["Disco"]      = typeof(DiscoPage),
            ["AppManager"] = typeof(AppManagerPage),
            ["Gaming"]     = typeof(GamingPage),
            ["Network"]    = typeof(NetworkPage),
            ["Driver"]     = typeof(DriverPage),
            ["Battery"]    = typeof(BatteryPage),
            ["Browser"]    = typeof(BrowserPage),
            ["Registry"]   = typeof(RegistryPage),
            ["Settings"]   = typeof(SettingsPage),
        };

        private static readonly Dictionary<string, string> NavTitles = new()
        {
            ["Hub"]        = "Dashboard",
            ["MainSuite"]  = "Ottimizzazione",
            ["Monitor"]    = "Monitor Live",
            ["Disco"]      = "Analisi Disco",
            ["AppManager"] = "Processi e App",
            ["Gaming"]     = "Gaming Mode",
            ["Network"]    = "Scanner Rete",
            ["Driver"]     = "Driver",
            ["Battery"]    = "Batteria",
            ["Browser"]    = "Pulizia Browser",
            ["Registry"]   = "Registro",
            ["Settings"]   = "Impostazioni",
        };

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            Title = "SysSuite One";

            _sysInfo = SystemInfo.Collect();

            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                    WinRT.Interop.WindowNative.GetWindowHandle(this)));
            appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(100, 100, 1200, 800));

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch { _cpuCounter = null; }

            ApplyVersionLabels();
            TxtHostname.Text = Environment.MachineName;

            // Mostra badge admin/non-admin nella status bar
            if (AdminHelper.IsAdmin())
            {
                BannerIsAdmin.Visibility  = Microsoft.UI.Xaml.Visibility.Visible;
                BannerNotAdmin.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
            else
            {
                BannerIsAdmin.Visibility  = Microsoft.UI.Xaml.Visibility.Collapsed;
                BannerNotAdmin.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }

            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += (_, _) => UpdateMetrics();
            _timer.Start();

            // Avvio: ultima tab o sempre Dashboard
            var _settings = SettingsService.Load();
            string startTag = _settings.RestoreLastTabOnStartup ? _settings.LastNavTag : "Hub";
            if (!NavPagesMap.ContainsKey(startTag)) startTag = "Hub";

            ContentFrame.Navigate(NavPagesMap[startTag]);
            SelectNavItemByTag(startTag);
            ApplyNavTitle(startTag);

            NavList.Loaded += (_, _) => ApplyNavItemVisuals(startTag);

            UpdateMetrics();
        }

        // ── Feedback visivo sidebar ──────────────────────────────────────
        private static readonly Windows.UI.Color _accentColor =
            Windows.UI.Color.FromArgb(255, 0, 212, 160);   // #00D4A0
        private static readonly Windows.UI.Color _accentDimColor =
            Windows.UI.Color.FromArgb(20, 0, 212, 160);    // #14 00D4A0
        private static readonly Windows.UI.Color _iconMutedColor =
            Windows.UI.Color.FromArgb(255, 122, 139, 168); // #7A8BA8
        private static readonly Windows.UI.Color _textSecColor =
            Windows.UI.Color.FromArgb(255, 176, 186, 208); // #B0BAD0
        private static readonly Windows.UI.Color _textPrimColor =
            Windows.UI.Color.FromArgb(255, 236, 240, 248); // #ECF0F8

        private void ApplyNavItemVisuals(string selectedTag)
        {
            foreach (var obj in NavList.Items)
            {
                if (obj is not ListViewItem lvi || lvi.Tag is not string tag) continue;
                bool selected = tag == selectedTag;

                // Background item
                lvi.Background = new SolidColorBrush(
                    selected ? _accentDimColor : Windows.UI.Color.FromArgb(0, 0, 0, 0));

                // Bordo sinistro accent (via BorderBrush + BorderThickness)
                lvi.BorderBrush = new SolidColorBrush(
                    selected ? _accentColor : Windows.UI.Color.FromArgb(0, 0, 0, 0));
                lvi.BorderThickness = selected
                    ? new Microsoft.UI.Xaml.Thickness(2, 0, 0, 0)
                    : new Microsoft.UI.Xaml.Thickness(0);

                // FontIcon e TextBlock dentro l'item
                var icon = FindFirstDescendant<Microsoft.UI.Xaml.Controls.FontIcon>(lvi);
                var text = FindFirstDescendant<Microsoft.UI.Xaml.Controls.TextBlock>(lvi);

                if (icon != null)
                    icon.Foreground = new SolidColorBrush(
                        selected ? _accentColor : _iconMutedColor);
                if (text != null)
                    text.Foreground = new SolidColorBrush(
                        selected ? _textPrimColor : _textSecColor);
            }
        }

        private static T? FindFirstDescendant<T>(Microsoft.UI.Xaml.DependencyObject parent)
            where T : Microsoft.UI.Xaml.DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindFirstDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        // ── Health Score ─────────────────────────────────────────────────
        private static int CalcHealthScore(float cpuPct, double ramPct, double diskFreeGB, double diskTotalGB)
        {
            int score = 100;

            // CPU: -30 punti se > 90%, -15 se > 70%, -5 se > 50%
            if      (cpuPct > 90) score -= 30;
            else if (cpuPct > 70) score -= 15;
            else if (cpuPct > 50) score -= 5;

            // RAM: -25 punti se > 90%, -12 se > 75%, -5 se > 60%
            if      (ramPct > 90) score -= 25;
            else if (ramPct > 75) score -= 12;
            else if (ramPct > 60) score -= 5;

            // Disco: -20 punti se < 5% libero, -10 se < 10%, -5 se < 20%
            double diskFreePct = diskTotalGB > 0 ? diskFreeGB / diskTotalGB * 100 : 100;
            if      (diskFreePct < 5)  score -= 20;
            else if (diskFreePct < 10) score -= 10;
            else if (diskFreePct < 20) score -= 5;

            return Math.Clamp(score, 0, 100);
        }

        /// <summary>
        /// Naviga a una pagina per tag e sincronizza la sidebar.
        /// Chiamabile da qualsiasi pagina tramite MainWindow.Instance?.NavigateTo(tag).
        /// </summary>
        public void NavigateTo(string tag)
        {
            if (!NavPagesMap.TryGetValue(tag, out var pageType)) return;
            ContentFrame.Navigate(pageType);
            SelectNavItemByTag(tag);
            ApplyNavTitle(tag);
            ApplyNavItemVisuals(tag);
            SettingsService.Update(s => s.LastNavTag = tag);
        }

        /// <summary>Ripristina stato interno notifiche dopo modifica soglie da Impostazioni.</summary>
        public void ResetProactiveNotificationState()
        {
            _notifiedDiskFull = false;
            _notifiedDrivers  = false;
            _highCpuStreak    = 0;
            _notifiedHighCpu  = false;
            _highRamStreak    = 0;
            _notifiedHighRam  = false;
        }

        
        /// <summary>
        /// Controlla condizioni critiche e invia notifiche Windows una sola volta
        /// per sessione (o finché la condizione persiste e viene risolta).
        /// </summary>
        private void CheckProactiveAlerts(float cpu)
        {
            try
            {
                var settings = SysSuite.Core.SettingsService.Load();
                if (settings.DndUntil.HasValue && settings.DndUntil.Value > DateTime.Now)
                    return;

                // ── Disco quasi pieno ─────────────────────────────────
                if (settings.NotifyDiskFull)
                {
                    if (_sysInfo.DiskUsedPct >= settings.DiskWarningPct)
                    {
                        if (!_notifiedDiskFull)
                        {
                            _notifiedDiskFull = true;
                            SysSuite.Core.ToastHelper.Send(
                                "⚠️ Disco quasi pieno — SysSuite",
                                $"Disco C: {_sysInfo.DiskUsedPct:0.#}% usato ({_sysInfo.DiskFreeGB:0.#} GB liberi). Apri Analisi Disco per liberare spazio.");
                        }
                    }
                    else
                    {
                        _notifiedDiskFull = false; // reset se situazione migliorata
                    }
                }

                // ── CPU alta prolungata (5 tick × 3s = ~15s) ─────────
                if (settings.NotifyHighCPU)
                {
                    if (cpu >= settings.CpuWarningPct)
                    {
                        _highCpuStreak++;
                        if (_highCpuStreak >= 5 && !_notifiedHighCpu)
                        {
                            _notifiedHighCpu = true;
                            SysSuite.Core.ToastHelper.Send(
                                "⚠️ CPU sotto pressione — SysSuite",
                                $"CPU al {cpu:0.#}% da oltre 15 secondi. Apri Monitor Live per identificare il processo.");
                        }
                    }
                    else
                    {
                        _highCpuStreak   = 0;
                        _notifiedHighCpu = false;
                    }
                }

                // ── RAM alta prolungata (stessa logica CPU: ~15s) ───────
                if (settings.NotifyHighRAM)
                {
                    double ram = _sysInfo.RAMUsedPct;
                    if (ram >= settings.RamWarningPct)
                    {
                        _highRamStreak++;
                        if (_highRamStreak >= 5 && !_notifiedHighRam)
                        {
                            _notifiedHighRam = true;
                            SysSuite.Core.ToastHelper.Send(
                                "⚠️ Memoria sotto pressione — SysSuite",
                                $"RAM al {ram:0.#}% usata. Apri Processi e App o Monitor Live per alleggerire.");
                        }
                    }
                    else
                    {
                        _highRamStreak   = 0;
                        _notifiedHighRam = false;
                    }
                }
                else
                {
                    _highRamStreak   = 0;
                    _notifiedHighRam = false;
                }

                // ── Driver problematici (check ogni 5 minuti) ─────────
                if (settings.NotifyDriverProblems &&
                    (DateTime.Now - _lastDriverCheck).TotalMinutes >= 5)
                {
                    _lastDriverCheck = DateTime.Now;
                    Task.Run(() =>
                    {
                        try
                        {
                            var svc = new SysSuite.Services.DriverService();
                            var bad = svc.GetProblematicDrivers();
                            if (bad.Count > 0 && !_notifiedDrivers)
                            {
                                _notifiedDrivers = true;
                                DispatcherQueue.TryEnqueue(() =>
                                    SysSuite.Core.ToastHelper.Send(
                                        $"⚠️ {bad.Count} driver problematic{(bad.Count == 1 ? "o" : "i")} — SysSuite",
                                        "Apri il modulo Driver per visualizzare i dettagli e creare un backup."));
                            }
                            else if (bad.Count == 0)
                            {
                                _notifiedDrivers = false;
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        private void ApplyVersionLabels()
        {
            var asm = Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version;
            TxtVersion.Text = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.0.0";

            try
            {
                var path = asm.Location;
                if (!string.IsNullOrEmpty(path))
                    TxtBuildDate.Text = $"build {File.GetLastWriteTime(path):yyyy.MM}";
            }
            catch { /* keep XAML default */ }
        }

        private static string FormatUptime(TimeSpan u)
        {
            if (u.TotalDays >= 1)
                return $"{u.Days}g {u.Hours}h {u.Minutes}m";
            if (u.TotalHours >= 1)
                return $"{u.Hours}h {u.Minutes}m";
            return $"{u.Minutes}m";
        }

        private void SelectNavItemByTag(string tag)
        {
            _suppressNavSelection = true;
            try
            {
                foreach (var item in NavList.Items)
                {
                    if (item is ListViewItem lvi && lvi.Tag is string t && t == tag)
                    {
                        NavList.SelectedItem = lvi;
                        _lastNavItem = lvi;
                        return;
                    }
                }
            }
            finally { _suppressNavSelection = false; }
        }

        private void ApplyNavTitle(string tag)
        {
            TxtTitle.Text = NavTitles.TryGetValue(tag, out var title) ? title : "Dashboard";
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressNavSelection)
                return;

            if (NavList.SelectedItem is ListViewItem lvi
                && lvi.Tag is string tag
                && NavPagesMap.TryGetValue(tag, out var pageType))
            {
                _lastNavItem = lvi;
                ContentFrame.Navigate(pageType);
                ApplyNavTitle(tag);
                ApplyNavItemVisuals(tag);
                SettingsService.Update(s => s.LastNavTag = tag);
                return;
            }

            if (_lastNavItem != null)
            {
                _suppressNavSelection = true;
                try { NavList.SelectedItem = _lastNavItem; }
                finally { _suppressNavSelection = false; }
            }
        }

        private void BtnRelaunchAdmin_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            AdminHelper.RelaunchAsAdmin();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo("Settings");
        }

        private bool _updatingMetrics;

        private async void UpdateMetrics()
        {
            if (_updatingMetrics) return;
            _updatingMetrics = true;
            try
            {
                float cpu = _cpuCounter != null
                    ? (float)Math.Round(_cpuCounter.NextValue(), 0) : 0;

                // WMI Refresh su background thread — non blocca la UI
                await Task.Run(() => _sysInfo.Refresh());

                TxtCPU.Text  = $"{cpu:0}%";
                TxtRAM.Text  = $"{_sysInfo.RAMUsedPct:0.#}%";
                TxtDisk.Text = $"{_sysInfo.DiskFreeGB:0.#} GB";
                TxtTime.Text = DateTime.Now.ToString("HH:mm:ss");

                TxtUptime.Text = $"Uptime: {FormatUptime(_sysInfo.Uptime)}";
                TxtStatus.Text = "Sistema pronto";

                // Health Score
                int health = CalcHealthScore(cpu, _sysInfo.RAMUsedPct,
                    _sysInfo.DiskFreeGB, _sysInfo.DiskTotalGB);
                TxtHealth.Text = health + "/100";
                TxtHealth.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    health >= 80
                        ? Windows.UI.Color.FromArgb(255, 52, 211, 153)
                        : health >= 50
                            ? Windows.UI.Color.FromArgb(255, 255, 181, 71)
                            : Windows.UI.Color.FromArgb(255, 255, 90, 90));

                // Notifiche proattive
                CheckProactiveAlerts(cpu);
            }
            catch { }
            finally { _updatingMetrics = false; }
        }
    }
}
