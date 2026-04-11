using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using SysSuite.Core;
using SysSuite.Services;
using SysSuite.ViewModels;

namespace SysSuite
{
    public partial class App : Application
    {
        internal MainWindow? _window;

        public static IServiceProvider Services { get; private set; } = null!;

        public App()
        {
            ConfigureLogging();
            ConfigureServices();
            UnhandledException += OnUnhandledException;
            InitializeComponent();
        }

        private static void ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<BatteryService>();
            services.AddSingleton<NetworkService>();
            services.AddSingleton<GamingService>();
            services.AddSingleton<DriverService>();
            services.AddSingleton<PrivacyService>();
            services.AddSingleton<BrowserService>();
            services.AddSingleton<DiskAnalyzer>();
            services.AddSingleton<CleanupService>();
            services.AddTransient<DiscoViewModel>();
            services.AddTransient<BatteryViewModel>();
            services.AddTransient<NetworkViewModel>();
            services.AddTransient<GamingViewModel>();
            services.AddTransient<DriverViewModel>();
            services.AddTransient<PrivacyViewModel>();
            services.AddTransient<BrowserViewModel>();
            services.AddSingleton<ProcessManager>();
            services.AddTransient<AppManagerViewModel>();
            Services = services.BuildServiceProvider();
        }

        private static void ConfigureLogging()
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SysSuite", "Logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "SysSuite_Log.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true)
                .CreateLogger();

            Log.Information("SysSuite One — logging avviato (cartella: {LogDir})", logDir);
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.Exception is Exception ex)
                    Log.Fatal(ex, "Eccezione non gestita: {Message}\n{StackTrace}", ex.Message, ex.StackTrace ?? "");
                else
                    Log.Fatal("Eccezione non gestita: {Message}", e.Message);
            }
            catch { }

            try { e.Handled = true; }
            catch { }

            try
            {
                DispatcherQueue? dq = DispatcherQueue.GetForCurrentThread();
                if (dq == null) return;
                dq.TryEnqueue(() => _ = ShowUnhandledExceptionDialogAsync());
            }
            catch { }
        }

        private async Task ShowUnhandledExceptionDialogAsync()
        {
            try
            {
                Microsoft.UI.Xaml.XamlRoot? xamlRoot = null;
                if (_window?.Content is FrameworkElement fe)
                    xamlRoot = fe.XamlRoot;
                else if (MainWindow.Instance?.Content is FrameworkElement fe2)
                    xamlRoot = fe2.XamlRoot;

                if (xamlRoot == null) return;

                var dlg = new ContentDialog
                {
                    Title = "SysSuite One",
                    Content = "Si è verificato un errore imprevisto. I dettagli sono stati salvati nei log dell'applicazione.",
                    CloseButtonText = "OK",
                    XamlRoot = xamlRoot
                };
                await dlg.ShowAsync();
            }
            catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            LiveCharts.Configure(config => config
                .AddSkiaSharp()
                .AddDefaultMappers()
                .AddDarkTheme());

            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Any(a => a.Equals("--boost", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var clean = new SysSuite.Services.CleanupService();
                    clean.CleanTemp();
                    clean.CleanThumbnails();
                    var browser = new SysSuite.Services.BrowserService();
                    foreach (var b in browser.DetectBrowsers().Where(b => b.Installed))
                        try { browser.CleanCache(b); } catch { }
                }
                catch (Exception ex) { Log.Warning(ex, "Modalità --boost: errore durante la pulizia"); }
                finally { Log.CloseAndFlush(); }
                Exit();
                return;
            }

            ToastHelper.Register();
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
