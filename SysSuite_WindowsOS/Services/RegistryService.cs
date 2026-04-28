using Microsoft.Win32;
using SysSuite.Core;
using System.Threading;
using System.Threading.Tasks;

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

        public List<Models.RegistryIssue> Scan(IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var issues = new List<Models.RegistryIssue>();
            foreach (var path in ScanPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(path);
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key == null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var entry = key.OpenSubKey(sub);
                        if (entry == null) continue;

                        // Controlla DLL mancanti
                        foreach (var val in entry.GetValueNames())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string? data = entry.GetValue(val)?.ToString();
                            if (data == null) continue;
                            if (TryExtractExecutablePath(data, out string executablePath) &&
                                !File.Exists(executablePath))
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

        public Task<List<Models.RegistryIssue>> ScanAsync(IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

        public async Task<string> BackupAsync(string backupName, CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SysSuite_Registry_Backup_{backupName}_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
            await ProcessRunner.RunAsync("reg.exe", $"export HKLM \"{path}\" /y", cancellationToken).ConfigureAwait(false);
            Emit($"Backup registro: {path}", "ok");
            return path;
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);

        private static bool TryExtractExecutablePath(string rawValue, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            string expanded = Environment.ExpandEnvironmentVariables(rawValue).Trim();
            var (fileName, _) = UninstallCommandParser.SplitExecutableAndArguments(expanded);
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            string trimmed = fileName.Trim().Trim('"');
            if (!(trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                return false;

            path = trimmed;
            return path.Contains('\\');
        }
    }
}
