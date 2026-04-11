using System.Management;
using System.Runtime.CompilerServices;
using System.Threading;
using SysSuite.Models;
using SerilogLogger = Serilog.Log;

namespace SysSuite.Services
{
    /// <summary>Analisi cartelle, file grandi, S.M.A.R.T.</summary>
    public class DiskAnalyzer
    {
        public event Action<string, string>? Log;

        /// <summary>Scansiona cartelle sotto <paramref name="root"/> e produce risultati in streaming (nessuna List monolitica).</summary>
        public async IAsyncEnumerable<DiskEntry> GetHeavyFoldersAsync(
            string root,
            int depth = 2,
            IProgress<string>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var searchOpt = depth == 1 ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root, "*", searchOpt).Take(500);
            }
            catch (Exception ex)
            {
                Log?.Invoke("Analisi cartelle: " + ex.Message, "err");
                SerilogLogger.Warning(ex, "Analisi cartelle: {Message}", ex.Message);
                yield break;
            }

            foreach (var dir in dirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(dir);
                DiskEntry? row = null;
                try
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    long size = 0;
                    foreach (var f in files)
                    {
                        try { size += new FileInfo(f).Length; }
                        catch { /* file singolo ignorato */ }
                    }

                    row = new DiskEntry
                    {
                        Path = dir,
                        Name = Path.GetFileName(dir),
                        SizeBytes = size,
                        FileCount = files.Length,
                        FolderCount = Directory.GetDirectories(dir).Length,
                        LastWrite = Directory.GetLastWriteTime(dir)
                    };
                }
                catch { /* cartella ignorata */ }

                if (row != null)
                {
                    yield return row;
                    await Task.Yield();
                }
            }
        }

        /// <summary>Scansiona file grandi sotto <paramref name="root"/> in streaming.</summary>
        public async IAsyncEnumerable<DiskEntry> GetLargeFilesAsync(
            string root,
            long minBytes = 100 * 1024 * 1024,
            IProgress<string>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Log?.Invoke("File grandi: " + ex.Message, "err");
                SerilogLogger.Warning(ex, "File grandi: {Message}", ex.Message);
                yield break;
            }

            foreach (var f in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(f);
                DiskEntry? entry = null;
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Length >= minBytes)
                    {
                        entry = new DiskEntry
                        {
                            Path = fi.DirectoryName ?? "",
                            Name = fi.Name,
                            SizeBytes = fi.Length,
                            LastWrite = fi.LastWriteTime
                        };
                    }
                }
                catch { /* file ignorato */ }

                if (entry != null)
                {
                    yield return entry;
                    await Task.Yield();
                }
            }
        }

        public List<SmartData> GetSmartData()
        {
            var result = new List<SmartData>();
            try
            {
                using var diskQ = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                using var failQ = new ManagementObjectSearcher(
                    "root\\wmi", "SELECT * FROM MSStorageDriver_FailurePredictStatus");

                foreach (ManagementObject disk in diskQ.Get())
                {
                    var sd = new SmartData
                    {
                        DiskModel = disk["Model"]?.ToString()?.Trim() ?? "",
                        SerialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "",
                        Interface = disk["InterfaceType"]?.ToString() ?? "",
                        SizeBytes = Convert.ToInt64(disk["Size"] ?? 0)
                    };

                    foreach (ManagementObject f in failQ.Get())
                    {
                        if (f["InstanceName"]?.ToString()?.Contains(
                            disk["Index"]?.ToString() ?? "", StringComparison.Ordinal) == true)
                        {
                            sd.FailPredicted = Convert.ToBoolean(f["PredictFailure"]);
                            break;
                        }
                    }

                    sd.Attributes["Reallocated Sectors"] = 0;
                    sd.Attributes["Pending Sectors"] = 0;
                    sd.Attributes["Uncorrectable Errors"] = 0;

                    result.Add(sd);
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke("SMART: " + ex.Message, "err");
                SerilogLogger.Warning(ex, "SMART: {Message}", ex.Message);
            }

            return result;
        }

        public Task<List<SmartData>> GetSmartDataAsync(CancellationToken cancellationToken = default) =>
            Task.Run(GetSmartData, cancellationToken);
    }
}
