using SysSuite.Core;
using Microsoft.Win32;

namespace SysSuite.Services
{
    /// <summary>Attiva/disattiva modalita' Gaming con ripristino completo.</summary>
    public class GamingService
    {
        public event Action<string,string>? Log;

        public bool IsActive { get; private set; } = false;

        private readonly PerformanceService _perf = new();
        private readonly NetworkService     _net  = new();
        private readonly ServicesManager    _svc  = new();

        private string _prevPowerPlan = "";

        private static readonly string[] XboxServices =
            { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" };

        private static readonly string[] BackgroundServices =
            { "wuauserv", "bits", "OneSyncSvc" };

        public GamingService()
        {
            // Registra i handler una sola volta per evitare log duplicati
            _perf.Log += (m, t) => Log?.Invoke(m, t);
            _net.Log  += (m, t) => Log?.Invoke(m, t);
        }

        public void Activate()
        {
            Emit("=== ATTIVAZIONE GAMING MODE ===", "head");

            _prevPowerPlan = _perf.GetCurrentPlan();
            _perf.SetUltimatePlan();

            EnableGameMode(true);
            Emit("Game Mode Windows abilitato", "ok");

            EnableHAGS(true);
            Emit("GPU Hardware Scheduling (HAGS) abilitato", "ok");

            _net.DisableNagle();

            foreach (var s in XboxServices)
                try { _svc.Disable(s); } catch { }

            foreach (var s in BackgroundServices)
                ProcessRunner.Run("net.exe", $"stop {s}");

            IsActive = true;
            Emit("Gaming Mode ATTIVA", "ok");
        }

        public void Deactivate()
        {
            Emit("=== RIPRISTINO MODALITA' STANDARD ===", "head");

            _perf.SetBalancedPlan();

            EnableGameMode(false);

            // HAGS lasciato attivo — migliora le prestazioni sempre
            Emit("GPU HAGS: lasciato attivo (migliora sempre le prestazioni)", "info");

            _net.RestoreNagle();

            foreach (var s in XboxServices)
                try { _svc.Enable(s, "auto"); } catch { }

            foreach (var s in BackgroundServices)
                ProcessRunner.Run("net.exe", $"start {s}");

            IsActive = false;
            Emit("Modalita' standard ripristinata", "ok");
        }

        private static void EnableGameMode(bool enable)
        {
            int val = enable ? 1 : 0;
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\GameBar", true);
            key?.SetValue("AutoGameModeEnabled", val);
            key?.SetValue("AllowAutoGameMode", val);
        }

        private static void EnableHAGS(bool enable)
        {
            using var key = Registry.LocalMachine.CreateSubKey(
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
            key?.SetValue("HwSchMode", enable ? 2 : 1);
        }


        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}