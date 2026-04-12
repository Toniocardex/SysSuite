using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog;
using SysSuite.Core;
using SysSuite.Interop;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>Processi, app avvio, disinstallazione, app inutilizzate.</summary>
    public class ProcessManager
    {
        public event Action<string, string>? Log;

        private readonly SemaphoreSlim _processReadGate = new(1, 1);

        /// <summary>Ultimo campionamento CPU (tempo kernel+user cumulativo, unità 100 ns).</summary>
        private readonly Dictionary<int, long> _prevProcCpu100Ns = new(512);

        /// <summary>Percorso immagine per PID (evita QueryFullProcessImageName ad ogni tick).</summary>
        private readonly Dictionary<int, string> _pathByPid = new(512);

        private readonly object _cpuSampleLock = new();
        private long _prevSampleQpc;

        /// <summary>Legge processi e metriche su thread pool (non blocca la UI).</summary>
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
            try
            {
                return GetProcessesViaNtQuery(filter);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Enumerazione NT fallita, fallback WMI/Process: {ex.Message}", "warn");
                return GetProcessesLegacy(filter);
            }
        }

        /// <summary>
        /// <see cref="NtQuerySystemInformation"/> + <see cref="MemoryMarshal"/> (zero-copy sul buffer nativo),
        /// CPU da delta tempi kernel/user (niente <see cref="PerformanceCounter"/> per processo).
        /// </summary>
        private List<ProcessEntry> GetProcessesViaNtQuery(string? filter)
        {
            List<NtProcessInfoReader.RawProcessRow> raw = NtProcessInfoReader.EnumerateProcesses();
            long nowQpc = Stopwatch.GetTimestamp();
            double wallSeconds;
            lock (_cpuSampleLock)
            {
                wallSeconds = _prevSampleQpc == 0
                    ? 0
                    : (nowQpc - _prevSampleQpc) / (double)Stopwatch.Frequency;
                _prevSampleQpc = nowQpc;
            }

            int cores = Math.Max(1, Environment.ProcessorCount);
            var livePids = new HashSet<int>(raw.Count);
            var result = new List<ProcessEntry>(raw.Count);

            foreach (NtProcessInfoReader.RawProcessRow row in raw)
            {
                livePids.Add(row.Pid);
                if (filter != null && !row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                long cpuTotal100Ns = row.UserTime100Ns + row.KernelTime100Ns;
                long prevTotal;
                lock (_cpuSampleLock)
                {
                    if (!_prevProcCpu100Ns.TryGetValue(row.Pid, out prevTotal))
                        prevTotal = cpuTotal100Ns;
                    _prevProcCpu100Ns[row.Pid] = cpuTotal100Ns;
                }

                long delta100Ns = cpuTotal100Ns - prevTotal;
                double cpuPct = 0;
                if (wallSeconds > 1e-6)
                {
                    // delta100Ns = tick da 100 ns; secondi CPU = delta / 1e7
                    cpuPct = 100.0 * (delta100Ns / 1e7) / wallSeconds / cores;
                    cpuPct = Math.Clamp(cpuPct, 0, 100);
                }

                DateTime start = row.CreateTime100Ns != 0
                    ? DateTime.FromFileTimeUtc(row.CreateTime100Ns)
                    : DateTime.MinValue;

                string path;
                lock (_cpuSampleLock)
                {
                    if (!_pathByPid.TryGetValue(row.Pid, out string? cached) || string.IsNullOrEmpty(cached))
                    {
                        path = ProcessImagePath.TryQuery(row.Pid);
                        if (!string.IsNullOrEmpty(path))
                            _pathByPid[row.Pid] = path;
                    }
                    else
                    {
                        path = cached;
                    }
                }

                result.Add(new ProcessEntry
                {
                    PID = row.Pid,
                    Name = row.Name,
                    CpuPercent = Math.Round(cpuPct, 1),
                    RamMB = Math.Round(row.WorkingSetBytes / 1_048_576.0, 1),
                    Threads = (int)row.ThreadCount,
                    Handles = (int)row.HandleCount,
                    Responding = true,
                    Path = path,
                    StartTime = start
                });
            }

            PruneCpuHistory(livePids);
            return result.OrderByDescending(p => p.RamMB).ToList();
        }

        private void PruneCpuHistory(HashSet<int> livePids)
        {
            lock (_cpuSampleLock)
            {
                foreach (int pid in _prevProcCpu100Ns.Keys.ToArray())
                {
                    if (!livePids.Contains(pid))
                    {
                        _prevProcCpu100Ns.Remove(pid);
                        _pathByPid.Remove(pid);
                    }
                }
            }
        }

        /// <summary>Percorso legacy (<see cref="Process.GetProcesses"/> + contatori) se NT fallisce.</summary>
        private List<ProcessEntry> GetProcessesLegacy(string? filter)
        {
            var result = new List<ProcessEntry>();
            Process[] current = Process.GetProcesses();
            try
            {
                foreach (Process p in current)
                {
                    try
                    {
                        if (filter != null && !p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        result.Add(new ProcessEntry
                        {
                            PID = p.Id,
                            Name = p.ProcessName,
                            CpuPercent = 0,
                            RamMB = Math.Round(p.WorkingSet64 / 1_048_576.0, 1),
                            Threads = p.Threads.Count,
                            Handles = p.HandleCount,
                            Responding = p.Responding,
                            Path = TryGetPath(p),
                            StartTime = TryGetStartTime(p)
                        });
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (Process p in current)
                {
                    try { p.Dispose(); } catch { }
                }
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
            catch (Exception ex)
            {
                Log?.Invoke($"Kill {pid}: {ex.Message}", "err");
                return false;
            }
        }

        public List<InstalledApp> GetInstalledApps(string? filter = null)
        {
            var result = new List<InstalledApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void ReadHive(RegistryKey root, string relativePath, string hiveLabel)
            {
                RegistryKey? uninstallRoot = null;
                try
                {
                    uninstallRoot = root.OpenSubKey(relativePath);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex,
                        "[App] Impossibile aprire ramo Uninstall. Hive={Hive}, Path={Path}",
                        hiveLabel, relativePath);
                    return;
                }

                using (uninstallRoot)
                {
                    if (uninstallRoot == null) return;

                    string[] subKeyNames;
                    try
                    {
                        subKeyNames = uninstallRoot.GetSubKeyNames();
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex,
                            "[App] Enumerazione sottochiavi fallita. Hive={Hive}, Path={Path}",
                            hiveLabel, relativePath);
                        return;
                    }

                    foreach (var sub in subKeyNames)
                    {
                        try
                        {
                            using var app = uninstallRoot.OpenSubKey(sub);
                            if (app == null) continue;

                            string? name = app.GetValue("DisplayName")?.ToString();
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

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

                            string uninstall = app.GetValue("UninstallString")?.ToString()?.Trim() ?? "";
                            string displayIcon = app.GetValue("DisplayIcon")?.ToString()?.Trim() ?? "";

                            result.Add(new InstalledApp
                            {
                                Name = name,
                                Version = app.GetValue("DisplayVersion")?.ToString() ?? "",
                                Publisher = app.GetValue("Publisher")?.ToString() ?? "",
                                InstallDate = app.GetValue("InstallDate")?.ToString() ?? "",
                                SizeBytes = sizeBytes,
                                UninstallString = uninstall,
                                DisplayIcon = displayIcon
                            });
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Warning(ex,
                                "[App] Chiave di registro corrotta o illeggibile — elemento saltato. Hive={Hive}, SubKey={SubKey}",
                                hiveLabel, sub);
                        }
                    }
                }
            }

            ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM");
            ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM-WOW64");
            ReadHive(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU");

            return result.OrderBy(a => a.Name).ToList();
        }

        /// <summary>
        /// Avvia il flusso di disinstallazione (finestra visibile, tipicamente con richiesta UAC).
        /// </summary>
        public async Task UninstallAppAsync(string uninstallString,
            CancellationToken cancellationToken = default)
        {
            var (fileName, arguments) = UninstallCommandParser.SplitExecutableAndArguments(uninstallString);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Impossibile ricavare l'eseguibile di disinstallazione.", nameof(uninstallString));
            await ProcessRunner.RunVisibleAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
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
            try
            {
                return p.MainModule?.FileName ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static DateTime TryGetStartTime(Process p)
        {
            try
            {
                return p.StartTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
