using System.Diagnostics;
using Microsoft.Win32;
using Serilog;
using SysSuite.Models;
using System.Threading;
using System.Threading.Tasks;

namespace SysSuite.Services
{
    /// <summary>Processi, app avvio, disinstallazione, app inutilizzate.</summary>
    public class ProcessManager
    {
        public event Action<string,string>? Log;

        // Contatori CPU per processo — campionamento a due tick
        private readonly Dictionary<int, System.Diagnostics.PerformanceCounter> _cpuCounters = new();
        private readonly object _counterLock = new();
        private readonly SemaphoreSlim _processReadGate = new(1, 1);

        /// <summary>Legge processi e contatori CPU su thread pool (non blocca la UI).</summary>
        public async Task<List<ProcessEntry>> GetProcessesAsync(string? filter = null,
            CancellationToken cancellationToken = default)
        {
            await _processReadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => GetProcesses(filter), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _processReadGate.Release();
            }
        }

        public List<ProcessEntry> GetProcesses(string? filter = null)
        {
            var result  = new List<ProcessEntry>();
            var current = Process.GetProcesses();

            // Ordina per RAM e crea contatori CPU solo per i top 50 processi
            // (PerformanceCounter è una risorsa pesante — crearli per 300+ processi è devastante)
            var topByRam = current
                .Select(p => { try { return (proc: p, ram: p.WorkingSet64); } catch { return (proc: p, ram: 0L); } })
                .OrderByDescending(x => x.ram)
                .Take(50)
                .Select(x => x.proc.Id)
                .ToHashSet();

            lock (_counterLock)
            {
                var activePids = new HashSet<int>(current.Select(p => p.Id));

                // Rimuovi contatori per processi terminati
                var dead = _cpuCounters.Keys.Where(pid => !activePids.Contains(pid)).ToList();
                foreach (var pid in dead)
                {
                    _cpuCounters[pid].Dispose();
                    _cpuCounters.Remove(pid);
                }

                // Crea contatori SOLO per i top 50 per RAM (non tutti)
                foreach (var p in current)
                {
                    if (_cpuCounters.ContainsKey(p.Id)) continue;
                    if (!topByRam.Contains(p.Id)) continue;
                    try
                    {
                        var ctr = new System.Diagnostics.PerformanceCounter(
                            "Process", "% Processor Time", p.ProcessName, true);
                        ctr.NextValue();
                        _cpuCounters[p.Id] = ctr;
                    }
                    catch { }
                }
            }

            foreach (var p in current)
            {
                try
                {
                    if (filter != null && !p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Leggi CPU% — dividi per core count per ottenere % reale
                    double cpuPct = 0;
                    lock (_counterLock)
                    {
                        if (_cpuCounters.TryGetValue(p.Id, out var ctr))
                        {
                            try
                            {
                                int cores = Environment.ProcessorCount;
                                cpuPct = Math.Round(ctr.NextValue() / cores, 1);
                                cpuPct = Math.Max(0, Math.Min(100, cpuPct));
                            }
                            catch { }
                        }
                    }

                    result.Add(new ProcessEntry
                    {
                        PID        = p.Id,
                        Name       = p.ProcessName,
                        CpuPercent = cpuPct,
                        RamMB      = Math.Round(p.WorkingSet64 / 1_048_576.0, 1),
                        Threads    = p.Threads.Count,
                        Handles    = p.HandleCount,
                        Responding = p.Responding,
                        Path       = TryGetPath(p),
                        StartTime  = TryGetStartTime(p)
                    });
                }
                catch { }
            }
            return result.OrderByDescending(p => p.RamMB).ToList();
        }

        public bool KillProcess(int pid)
        {
            try
            {
                Process.GetProcessById(pid).Kill();
                Log?.Invoke($"Processo {pid} terminato", "ok");
                return true;
            }
            catch (Exception ex) { Log?.Invoke($"Kill {pid}: {ex.Message}", "err"); return false; }
        }

        public List<StartupEntry> GetStartupEntries()
        {
            var result = new List<StartupEntry>();
            var regPaths = new[]
            {
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",     "HKCU Run"),
                (@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",     "HKLM Run"),
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce"),
                (@"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce"),
            };
            foreach (var (path, label) in regPaths)
            {
                var root = path.StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
                var sub  = path[(path.IndexOf('\\')+1)..];
                using var key = root.OpenSubKey(sub);
                if (key == null) continue;
                foreach (var name in key.GetValueNames())
                    result.Add(new StartupEntry { Name = name, Command = key.GetValue(name)?.ToString() ?? "", Source = label, RegPath = path });
            }
            // Cartella avvio
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            foreach (var f in Directory.GetFiles(startupFolder))
                result.Add(new StartupEntry { Name = Path.GetFileName(f), Command = f, Source = "Cartella Avvio", RegPath = startupFolder });

            return result;
        }

        public bool DisableStartup(StartupEntry entry)
        {
            try
            {
                if (entry.Source.StartsWith("HKCU") || entry.Source.StartsWith("HKLM"))
                {
                    var root = entry.Source.StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
                    var sub  = entry.RegPath[(entry.RegPath.IndexOf('\\')+1)..];
                    using var key = root.OpenSubKey(sub, true);
                    key?.DeleteValue(entry.Name, false);
                }
                else
                {
                    string disabled = Path.Combine(entry.RegPath, $"_disabled_{entry.Name}");
                    File.Move(entry.Command, disabled);
                }
                Log?.Invoke($"Disabilitato: {entry.Name}", "ok");
                return true;
            }
            catch (Exception ex) { Log?.Invoke($"Errore: {ex.Message}", "err"); return false; }
        }

        public List<InstalledApp> GetInstalledApps(string? filter = null)
        {
            var result = new List<InstalledApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void ReadHive(RegistryKey root, string relativePath, string hiveLabel)
            {
                try
                {
                    using var key = root.OpenSubKey(relativePath);
                    if (key == null) return;

                    foreach (var sub in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var app = key.OpenSubKey(sub);
                            if (app == null) continue;

                            string? name = app.GetValue("DisplayName")?.ToString();
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                Serilog.Log.Warning(
                                    "[App] DisplayName mancante o vuoto. Hive={Hive}, SubKey={SubKey}",
                                    hiveLabel, sub);
                                continue;
                            }

                            if (!seen.Add(name))
                                continue;
                            if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (name.StartsWith("Update for", StringComparison.OrdinalIgnoreCase)
                                || name.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase))
                                continue;

                            long sizeBytes = 0;
                            try
                            {
                                sizeBytes = Convert.ToInt64(app.GetValue("EstimatedSize") ?? 0) * 1024;
                            }
                            catch (Exception ex)
                            {
                                Serilog.Log.Warning(ex,
                                    "[App] EstimatedSize non valido. Hive={Hive}, SubKey={SubKey}, DisplayName={Name}",
                                    hiveLabel, sub, name);
                            }

                            result.Add(new InstalledApp
                            {
                                Name = name,
                                Version = app.GetValue("DisplayVersion")?.ToString() ?? "",
                                Publisher = app.GetValue("Publisher")?.ToString() ?? "",
                                InstallDate = app.GetValue("InstallDate")?.ToString() ?? "",
                                SizeBytes = sizeBytes,
                                UninstallString = app.GetValue("UninstallString")?.ToString() ?? ""
                            });
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Warning(ex,
                                "[App] Errore lettura singola app. Hive={Hive}, SubKey={SubKey}",
                                hiveLabel, sub);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex,
                        "[App] Errore apertura ramo Uninstall. Hive={Hive}, Path={Path}",
                        hiveLabel, relativePath);
                }
            }

            ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM");
            ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM-WOW64");
            ReadHive(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU");

            return result.OrderBy(a => a.Name).ToList();
        }

        public List<InstalledApp> GetUnusedApps(int minDays = 90)
        {
            var apps = GetInstalledApps();
            var cutoff = DateTime.Now.AddDays(-minDays);
            var result = new List<InstalledApp>();

            foreach (var app in apps)
            {
                try
                {
                    var loc = Registry.LocalMachine
                        .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                        ?.OpenSubKey(app.Name)
                        ?.GetValue("InstallLocation")?.ToString();
                    if (string.IsNullOrEmpty(loc) || !Directory.Exists(loc)) continue;

                    var exes = Directory.GetFiles(loc, "*.exe", SearchOption.AllDirectories)
                        .Take(5).Select(f => new FileInfo(f));
                    var lastAccess = exes.Max(f => f.LastAccessTime);
                    if (lastAccess < cutoff)
                    {
                        app.DaysUnused = (int)(DateTime.Now - lastAccess).TotalDays;
                        result.Add(app);
                    }
                }
                catch { }
            }
            return result.OrderByDescending(a => a.DaysUnused).ToList();
        }

        private static string TryGetPath(Process p)
        {
            try { return p.MainModule?.FileName ?? ""; } catch { return ""; }
        }
        private static DateTime TryGetStartTime(Process p)
        {
            try { return p.StartTime; } catch { return DateTime.MinValue; }
        }
    }
}