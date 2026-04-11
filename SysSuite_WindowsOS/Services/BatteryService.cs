using System.Management;
using System.Threading;
using System.Threading.Tasks;
using SysSuite.Core;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>Salute batteria, cicli, report powercfg.</summary>
    public class BatteryService
    {
        public event Action<string, string>? Log;

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
                        CycleCount     = 0
                    };
                }
            }
            catch (Exception ex) { Log?.Invoke($"Batteria: {ex.Message}", "err"); }
            return null;
        }

        /// <summary>Interrogazione WMI su thread pool (non blocca la UI).</summary>
        public Task<BatteryInfo?> GetBatteryInfoAsync(CancellationToken cancellationToken = default) =>
            Task.Run(GetBatteryInfo, cancellationToken);

        /// <summary>Genera il report HTML con powercfg (cattura output, verifica codice di uscita).</summary>
        public async Task<string> GenerateReportAsync(CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "SysSuite_BatteryReport.html");
            string args = $"/batteryreport /output \"{path}\"";
            var (exit, output) = await ProcessRunner.RunCaptureAsync("powercfg.exe", args, cancellationToken)
                .ConfigureAwait(false);
            if (exit != 0)
            {
                string detail = string.IsNullOrWhiteSpace(output) ? "(nessun output)" : output.Trim();
                throw new InvalidOperationException($"powercfg terminato con codice {exit}. {detail}");
            }

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

        public Task<bool> HasBatteryAsync(CancellationToken cancellationToken = default) =>
            Task.Run(HasBattery, cancellationToken);
    }
}
