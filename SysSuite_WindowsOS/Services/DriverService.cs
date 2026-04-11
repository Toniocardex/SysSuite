using System.Management;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>Lista driver, driver problematici, backup con DISM.</summary>
    public class DriverService
    {
        public event Action<string,string>? Log;


        /// <summary>
        /// Converte la data WMI (20231201000000.000000+000) in formato leggibile (01/12/2023).
        /// Restituisce stringa vuota se il parsing fallisce.
        /// </summary>
        private static string ParseWmiDate(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length < 8) return "";
            try
            {
                // Formato WMI: YYYYMMDDHHMMSS.ffffff+UUU
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

        public string BackupDrivers()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SysSuite_Drivers_Backup_{DateTime.Now:yyyyMMdd}");
            var p = new System.Diagnostics.Process
            {
                StartInfo = new("DISM.exe", $"/Online /Export-Driver /Destination:\"{path}\"")
                    { UseShellExecute = true }
            };
            p.Start(); p.WaitForExit();
            Log?.Invoke($"Backup driver: {path}", "ok");
            return path;
        }
    }
}