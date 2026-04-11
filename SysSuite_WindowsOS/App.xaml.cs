using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using SysSuite.Core;

namespace SysSuite
{
    public partial class App : Application
    {
        internal MainWindow? _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Inizializza LiveCharts2 con tema scuro coerente con la palette dell'app
            LiveCharts.Configure(config => config
                .AddSkiaSharp()
                .AddDefaultMappers()
                .AddDarkTheme());

            // Modalità headless: --boost esegue pulizia rapida e termina
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
                catch { }
                Exit();
                return;
            }

            ToastHelper.Register();
            _window = new MainWindow();
            _window.Activate();
        }
    }
}