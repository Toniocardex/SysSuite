using System.Management;
using System.Threading;
using System.Threading.Tasks;
using SysSuite.Core;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>Lista driver, driver problematici, backup con DISM.</summary>
    public class DriverService
    {
        public event Action<string, string>? Log;

        /// <summary>
        /// Converte la data WMI (20231201000000.000000+000) in formato leggibile (01/12/2023).
        /// Restituisce stringa vuota se il parsing fallisce.
        /// </summary>
        private static string ParseWmiDate(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length < 8) return "";
            try
            {
                int y = int.Parse(raw.Substring(0, 4));
                int m = int.Parse(raw.Substring(4, 2));
                int d = int.Parse(raw.Substring(6, 2));
                if (y < 1990 || m < 1 || m > 12 || d < 1 || d > 31) return "";
                return new DateTime(y, m, d).ToString("dd/MM/yyyy");
            }
            catch { return raw.Length >= 8 ? raw.Substring(6, 2) + "/" + raw.Substring(4, 2) + "/" + raw.Substring(0, 4) : ""; }
        }

        public List<DriverEntry> GetDrivers()
        {
            var result = new List<DriverEntry>();
            try
            {
                using var q = new ManagementObjectSearcher("SELECT * FROM Win32_PnPSignedDriver");
                foreach (ManagementObject o in q.Get())
                {
                    result.Add(new DriverEntry
                    {
                        Name         = o["DeviceName"]?.ToString()?.Trim() ?? "",
                        Description  = o["Description"]?.ToString() ?? "",
                        Manufacturer = o["Manufacturer"]?.ToString() ?? "",
                        Version      = o["DriverVersion"]?.ToString() ?? "",
                        Date         = ParseWmiDate(o["DriverDate"]?.ToString() ?? ""),
                        DeviceClass  = o["DeviceClass"]?.ToString() ?? "",
                        HasProblem   = false,
                        Status       = "OK"
                    });
                }
            }
            catch (Exception ex) { Log?.Invoke($"Driver: {ex.Message}", "err"); }
            return result.OrderBy(d => d.DeviceClass).ThenBy(d => d.Name).ToList();
        }

        /// <summary>Esegue la scansione WMI su thread pool (non blocca la UI).</summary>
        public Task<List<DriverEntry>> GetDriversAsync(CancellationToken cancellationToken = default) =>
            Task.Run(GetDrivers, cancellationToken);

        public List<DriverEntry> GetProblematicDrivers()
        {
            var result = new List<DriverEntry>();
            try
            {
                using var q = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
                foreach (ManagementObject o in q.Get())
                {
                    result.Add(new DriverEntry
                    {
                        Name       = o["Name"]?.ToString() ?? "",
                        Status     = $"Errore {o["ConfigManagerErrorCode"]}",
                        HasProblem = true
                    });
                }
            }
            catch { }
            return result;
        }

        public Task<List<DriverEntry>> GetProblematicDriversAsync(CancellationToken cancellationToken = default) =>
            Task.Run(GetProblematicDrivers, cancellationToken);

        /// <summary>Backup driver con DISM (richiede admin). Output catturato per verificare il codice di uscita.</summary>
        public async Task<string> BackupDriversAsync(CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SysSuite_Drivers_Backup_{DateTime.Now:yyyyMMdd}");
            string args = $"/Online /Export-Driver /Destination:\"{path}\"";
            var (exit, output) = await ProcessRunner.RunCaptureAsync("DISM.exe", args, cancellationToken)
                .ConfigureAwait(false);
            if (exit != 0)
            {
                string detail = string.IsNullOrWhiteSpace(output) ? "(nessun output)" : output.Trim();
                throw new InvalidOperationException($"DISM terminato con codice {exit}. {detail}");
            }

            Log?.Invoke($"Backup driver: {path}", "ok");
            return path;
        }
    }
}
