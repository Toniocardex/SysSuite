namespace SysSuite.Services
{
    /// <summary>Rileva browser installati e pulisce cache, cookie, cronologia.</summary>
    public class BrowserService
    {
        public event Action<string,string>? Log;

        public record BrowserInfo(string Name, bool Installed, string CachePath, string DataPath);

        public List<BrowserInfo> DetectBrowsers()
        {
            string local  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            return new List<BrowserInfo>
            {
                new("Chrome",   Dir(local,  @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(local,  @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(local,  @"Google\Chrome\User Data\Default")),
                new("Edge",     Dir(local,  @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(local,  @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(local,  @"Microsoft\Edge\User Data\Default")),
                new("Firefox",  Dir(roaming,@"Mozilla\Firefox\Profiles"),
                    FindFirefoxCache(roaming),
                    Path.Combine(roaming, @"Mozilla\Firefox\Profiles")),
                new("Brave",    Dir(local,  @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                    Path.Combine(local,  @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                    Path.Combine(local,  @"BraveSoftware\Brave-Browser\User Data\Default")),
                new("Opera",    Dir(roaming,@"Opera Software\Opera Stable\Cache"),
                    Path.Combine(roaming,@"Opera Software\Opera Stable\Cache"),
                    Path.Combine(roaming,@"Opera Software\Opera Stable")),
            };
        }

        public void CleanCache(BrowserInfo browser)
        {
            if (!browser.Installed) { Emit($"{browser.Name} non installato", "warn"); return; }
            DeleteFolder(browser.CachePath, $"cache {browser.Name}");
        }

        public void CleanAll(BrowserInfo browser)
        {
            CleanCache(browser);
            // Cookie e sessioni (solo subcartelle sicure)
            foreach (var sub in new[] { "Code Cache", "GPUCache", "ShaderCache" })
            {
                string path = Path.Combine(browser.DataPath, sub);
                DeleteFolder(path, $"{browser.Name}/{sub}");
            }
        }

        public long GetCacheSize(BrowserInfo browser)
        {
            if (!browser.Installed || !Directory.Exists(browser.CachePath)) return 0;
            return Directory.GetFiles(browser.CachePath, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }

        private void DeleteFolder(string path, string label)
        {
            if (!Directory.Exists(path)) return;
            int deleted = 0;
            foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                try { File.Delete(f); deleted++; } catch { }
            Emit($"{label} rimossa ({deleted} file)", "ok");
        }

        private static bool Dir(string root, string sub) =>
            Directory.Exists(Path.Combine(root, sub));

        private static string FindFirefoxCache(string roaming)
        {
            string profiles = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
            if (!Directory.Exists(profiles)) return "";
            return Directory.GetDirectories(profiles).FirstOrDefault() ?? "";
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}