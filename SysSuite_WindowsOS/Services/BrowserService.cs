using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SysSuite.Services
{
    /// <summary>Eccezione sollevata quando la pulizia non può proseguire perché il browser è ancora in esecuzione.</summary>
    public sealed class BrowserRunningException : InvalidOperationException
    {
        public string BrowserName { get; }

        public BrowserRunningException(string browserName)
            : base($"Il browser {browserName} è in esecuzione.")
        {
            BrowserName = browserName;
        }
    }

    /// <summary>Rileva browser installati e pulisce cache, cookie, cronologia.</summary>
    public class BrowserService
    {
        public event Action<string, string>? Log;

        public record BrowserInfo(string Name, bool Installed, string CachePath, string DataPath);

        /// <summary>Nomi processo (senza .exe) usati da <see cref="Process.GetProcessesByName"/>.</summary>
        private static readonly IReadOnlyDictionary<string, string[]> ProcessNamesByBrowser =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Chrome"] = new[] { "chrome" },
                ["Edge"] = new[] { "msedge" },
                ["Firefox"] = new[] { "firefox" },
                ["Brave"] = new[] { "brave" },
                ["Opera"] = new[] { "opera", "opera_autoupdate" },
            };

        public List<BrowserInfo> DetectBrowsers()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            return new List<BrowserInfo>
            {
                new("Chrome", Dir(local, @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(local, @"Google\Chrome\User Data\Default")),
                new("Edge", Dir(local, @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(local, @"Microsoft\Edge\User Data\Default")),
                new("Firefox", Dir(roaming, @"Mozilla\Firefox\Profiles"),
                    FindFirefoxCache(roaming),
                    Path.Combine(roaming, @"Mozilla\Firefox\Profiles")),
                new("Brave", Dir(local, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                    Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                    Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data\Default")),
                new("Opera", Dir(roaming, @"Opera Software\Opera Stable\Cache"),
                    Path.Combine(roaming, @"Opera Software\Opera Stable\Cache"),
                    Path.Combine(roaming, @"Opera Software\Opera Stable")),
            };
        }

        public Task<List<BrowserInfo>> DetectBrowsersAsync(CancellationToken cancellationToken = default) =>
            Task.Run(DetectBrowsers, cancellationToken);

        /// <summary>True se almeno un processo noto del browser è in esecuzione (controllo via <see cref="Process.GetProcessesByName"/>).</summary>
        public static bool IsBrowserProcessRunning(BrowserInfo browser)
        {
            if (!ProcessNamesByBrowser.TryGetValue(browser.Name, out var names))
                return false;
            foreach (var n in names)
            {
                try
                {
                    if (Process.GetProcessesByName(n).Length > 0)
                        return true;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex,
                        "Impossibile verificare i processi per il nome {ProcessName} (browser {Browser}).",
                        n, browser.Name);
                }
            }

            return false;
        }

        /// <summary>
        /// Verifica i processi del browser prima di qualsiasi eliminazione su disco.
        /// </summary>
        /// <exception cref="BrowserRunningException">Il browser risulta in esecuzione.</exception>
        public static void EnsureBrowserNotRunningForCleaning(BrowserInfo browser)
        {
            if (!IsBrowserProcessRunning(browser))
                return;
            Serilog.Log.Warning(
                "Pulizia cache bloccata: il browser {Browser} risulta in esecuzione (processo attivo).",
                browser.Name);
            throw new BrowserRunningException(browser.Name);
        }

        public void CleanCache(BrowserInfo browser, IProgress<(string Message, string Type)>? progress = null)
        {
            if (!browser.Installed)
            {
                Report(browser.Name + " non installato", "warn", progress);
                return;
            }

            EnsureBrowserNotRunningForCleaning(browser);
            DeleteFolder(browser.CachePath, "cache " + browser.Name, progress);
        }

        public Task CleanCacheAsync(BrowserInfo browser,
            IProgress<(string Message, string Type)>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => CleanCache(browser, progress), cancellationToken);

        public void CleanAll(BrowserInfo browser, IProgress<(string Message, string Type)>? progress = null)
        {
            if (!browser.Installed)
            {
                Report(browser.Name + " non installato", "warn", progress);
                return;
            }

            EnsureBrowserNotRunningForCleaning(browser);
            CleanCache(browser, progress);
            foreach (var sub in new[] { "Code Cache", "GPUCache", "ShaderCache" })
            {
                string path = Path.Combine(browser.DataPath, sub);
                DeleteFolder(path, browser.Name + "/" + sub, progress);
            }
        }

        public Task CleanAllAsync(BrowserInfo browser,
            IProgress<(string Message, string Type)>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => CleanAll(browser, progress), cancellationToken);

        public long GetCacheSize(BrowserInfo browser)
        {
            if (!browser.Installed || !Directory.Exists(browser.CachePath)) return 0;
            return Directory.GetFiles(browser.CachePath, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch (Exception ex)
                    {
                        Serilog.Log.Debug(ex, "Dimensione cache: file non leggibile {Path}", f);
                        return 0L;
                    }
                });
        }

        public Task<long> GetCacheSizeAsync(BrowserInfo browser, CancellationToken cancellationToken = default) =>
            Task.Run(() => GetCacheSize(browser), cancellationToken);

        private void DeleteFolder(string path, string label, IProgress<(string Message, string Type)>? progress)
        {
            if (!Directory.Exists(path)) return;
            int deleted = 0;
            foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(f);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Eliminazione file cache non riuscita (file in uso o permessi): {Path}", f);
                }
            }

            Report(label + " rimossa (" + deleted + " file)", "ok", progress);
        }

        private void Report(string msg, string type, IProgress<(string Message, string Type)>? progress)
        {
            progress?.Report((msg, type));
            Log?.Invoke(msg, type);
        }

        private static bool Dir(string root, string sub) =>
            Directory.Exists(Path.Combine(root, sub));

        private static string FindFirefoxCache(string roaming)
        {
            string profiles = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
            if (!Directory.Exists(profiles)) return "";
            return Directory.GetDirectories(profiles).FirstOrDefault() ?? "";
        }
    }
}
