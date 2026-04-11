using Microsoft.Win32;

namespace SysSuite.Services
{
    /// <summary>Scansione chiavi orfane, backup e restore del registro.</summary>
    public class RegistryService
    {
        public event Action<string,string>? Log;

        // Percorsi noti con chiavi orfane comuni
        private static readonly string[] ScanPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            @"SYSTEM\CurrentControlSet\Services",
        };

        public List<Models.RegistryIssue> Scan(IProgress<string>? progress = null)
        {
            var issues = new List<Models.RegistryIssue>();
            foreach (var path in ScanPaths)
            {
                progress?.Report(path);
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key == null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var entry = key.OpenSubKey(sub);
                        if (entry == null) continue;

                        // Controlla DLL mancanti
                        foreach (var val in entry.GetValueNames())
                        {
                            string? data = entry.GetValue(val)?.ToString();
                            if (data == null) continue;
                            if ((data.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                                 data.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) &&
                                data.Contains('\\') && !File.Exists(data.Trim('"')))
                            {
                                issues.Add(new Models.RegistryIssue
                                {
                                    Category    = "File mancante",
                                    KeyPath     = $@"HKLM\{path}\{sub}",
                                    ValueName   = val,
                                    Description = $"File non trovato: {data}"
                                });
                            }
                        }
                    }
                }
                catch { }
            }
            Emit($"Scansione completata: {issues.Count} problemi trovati", "ok");
            return issues;
        }

        public string Backup(string backupName)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SysSuite_Registry_Backup_{backupName}_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
            var p = new System.Diagnostics.Process
            {
                StartInfo = new("reg.exe", $"export HKLM \"{path}\" /y")
                    { CreateNoWindow = true, UseShellExecute = false }
            };
            p.Start(); p.WaitForExit();
            Emit($"Backup registro: {path}", "ok");
            return path;
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}