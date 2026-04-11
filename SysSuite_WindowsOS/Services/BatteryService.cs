using System.Management;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>Salute batteria, cicli, report powercfg.</summary>
    public class BatteryService
    {
        public event Action<string,string>? Log;

        public BatteryInfo? GetBatteryInfo()
        {
            try
            {
                using var q = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                foreach (ManagementObject o in q.Get())
                {
                    return new BatteryInfo
                    {
                        Name           = o["Name"]?.ToString() ?? "Batteria",
                        DesignCapacity = Convert.ToInt32(o["DesignCapacity"] ?? 0),
                        FullCapacity   = Convert.ToInt32(o["FullChargeCapacity"] ?? 0),
                        CurrentCharge  = Convert.ToInt32(o["EstimatedChargeRemaining"] ?? 0),
                        Status         = TranslateBatteryStatus(o["BatteryStatus"]?.ToString() ?? ""),
                        CycleCount     = 0  // Non disponibile via WMI standard
                    };
                }
            }
            catch (Exception ex) { Log?.Invoke($"Batteria: {ex.Message}", "err"); }
            return null;
        }

        public string GenerateReport()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "SysSuite_BatteryReport.html");
            var p = new System.Diagnostics.Process
            {
                StartInfo = new("powercfg.exe", $"/batteryreport /output \"{path}\"")
                    { CreateNoWindow = true, UseShellExecute = false }
            };
            p.Start(); p.WaitForExit();
            Log?.Invoke($"Report batteria: {path}", "ok");
            return path;
        }


        private static string TranslateBatteryStatus(string raw)
        {
            return raw switch
            {
                "1"  => "Scaricamento",
                "2"  => "In carica (CA)",
                "3"  => "Completamente carica",
                "4"  => "Bassa",
                "5"  => "Critica",
                "6"  => "Carica in corso",
                "7"  => "Carica e scarica",
                "8"  => "Nessuna batteria",
                "9"  => "Alta",
                "10" => "Sconosciuto",
                "11" => "Parzialmente carica",
                _    => raw.Length > 0 ? raw : "Sconosciuto"
            };
        }
        public bool HasBattery()
        {
            try
            {
                using var q = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                return q.Get().Count > 0;
            }
            catch { return false; }
        }
    }
}