using System.Management;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>Analisi cartelle, file grandi, S.M.A.R.T.</summary>
    public class DiskAnalyzer
    {
        public event Action<string,string>? Log;

        public List<DiskEntry> GetHeavyFolders(string root, int depth = 2,
            IProgress<string>? progress = null)
        {
            var results = new List<DiskEntry>();
            try
            {
                var dirs = Directory.EnumerateDirectories(root, "*",
                    depth == 1 ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories)
                    .Take(500);

                foreach (var dir in dirs)
                {
                    progress?.Report(dir);
                    try
                    {
                        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                        long size  = files.Sum(f => new FileInfo(f).Length);
                        results.Add(new DiskEntry
                        {
                            Path        = dir,
                            Name        = Path.GetFileName(dir),
                            SizeBytes   = size,
                            FileCount   = files.Length,
                            FolderCount = Directory.GetDirectories(dir).Length,
                            LastWrite   = Directory.GetLastWriteTime(dir)
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log?.Invoke($"Analisi: {ex.Message}", "err"); }

            return results.OrderByDescending(d => d.SizeBytes).Take(100).ToList();
        }

        public List<DiskEntry> GetLargeFiles(string root, long minBytes = 100 * 1024 * 1024,
            IProgress<string>? progress = null)
        {
            var results = new List<DiskEntry>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    progress?.Report(f);
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length >= minBytes)
                            results.Add(new DiskEntry
                            {
                                Path      = fi.DirectoryName ?? "",
                                Name      = fi.Name,
                                SizeBytes = fi.Length,
                                LastWrite = fi.LastWriteTime
                            });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log?.Invoke($"File grandi: {ex.Message}", "err"); }

            return results.OrderByDescending(d => d.SizeBytes).Take(200).ToList();
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
                        DiskModel    = disk["Model"]?.ToString()?.Trim() ?? "",
                        SerialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "",
                        Interface    = disk["InterfaceType"]?.ToString() ?? "",
                        SizeBytes    = Convert.ToInt64(disk["Size"] ?? 0)
                    };

                    foreach (ManagementObject f in failQ.Get())
                    {
                        if (f["InstanceName"]?.ToString()?.Contains(
                            disk["Index"]?.ToString() ?? "") == true)
                        {
                            sd.FailPredicted = Convert.ToBoolean(f["PredictFailure"]);
                            break;
                        }
                    }

                    // Attributi critici noti
                    sd.Attributes["Reallocated Sectors"] = 0;
                    sd.Attributes["Pending Sectors"]     = 0;
                    sd.Attributes["Uncorrectable Errors"]= 0;

                    result.Add(sd);
                }
            }
            catch (Exception ex) { Log?.Invoke($"SMART: {ex.Message}", "err"); }
            return result;
        }
    }
}