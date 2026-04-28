using SysSuite.Core;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;

namespace SysSuite.Services
{
    /// <summary>Attiva/disattiva modalita' Gaming con ripristino completo.</summary>
    public class GamingService
    {
        public event Action<string,string>? Log;

        public bool IsActive { get; private set; } = false;

        private readonly PerformanceService _perf;
        private readonly NetworkService _net;
        private readonly ServicesManager _svc;

        private string _prevPowerPlan = "";

        private static readonly string[] XboxServices =
            { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" };

        private static readonly string[] BackgroundServices =
            { "wuauserv", "bits", "OneSyncSvc" };

        public GamingService(PerformanceService perf, NetworkService net, ServicesManager servicesManager)
        {
            _perf = perf;
            _net = net;
            _svc = servicesManager;
            // Registra i handler una sola volta per evitare log duplicati
            _perf.Log += (m, t) => Log?.Invoke(m, t);
            _net.Log  += (m, t) => Log?.Invoke(m, t);
        }

        public async Task ActivateAsync(CancellationToken cancellationToken = default)
        {
            Emit("=== ATTIVAZIONE GAMING MODE ===", "head");

            _prevPowerPlan = await _perf.GetCurrentPlanAsync(cancellationToken).ConfigureAwait(false);
            await _perf.SetUltimatePlanAsync(cancellationToken).ConfigureAwait(false);

            EnableGameMode(true);
            Emit("Game Mode Windows abilitato", "ok");

            EnableHAGS(true);
            Emit("GPU Hardware Scheduling (HAGS) abilitato", "ok");

            _net.DisableNagle();

            foreach (string s in XboxServices)
            {
                try
                {
                    await _svc.DisableAsync(s, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    /* continua con gli altri servizi Xbox */
                }
            }

            foreach (var s in BackgroundServices)
                await ProcessRunner.RunAsync("net.exe", $"stop {s}", cancellationToken).ConfigureAwait(false);

            IsActive = true;
            Emit("Gaming Mode ATTIVA", "ok");
        }

        public async Task DeactivateAsync(CancellationToken cancellationToken = default)
        {
            Emit("=== RIPRISTINO MODALITA' STANDARD ===", "head");

            // Ripristina il piano precedente all'attivazione Gaming Mode.
            // Se era Ultimate Performance lo reimpostiamo, altrimenti torniamo a Balanced.
            bool wasUltimate = _prevPowerPlan.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)
                            || _prevPowerPlan.Contains("Prestazioni elevate", StringComparison.OrdinalIgnoreCase)
                            || _prevPowerPlan.Contains("High performance", StringComparison.OrdinalIgnoreCase);
            if (wasUltimate)
                await _perf.SetUltimatePlanAsync(cancellationToken).ConfigureAwait(false);
            else
                await _perf.SetBalancedPlanAsync(cancellationToken).ConfigureAwait(false);

            EnableGameMode(false);

            // HAGS lasciato attivo — migliora le prestazioni sempre
            Emit("GPU HAGS: lasciato attivo (migliora sempre le prestazioni)", "info");

            _net.RestoreNagle();

            foreach (string s in XboxServices)
            {
                try
                {
                    await _svc.EnableAsync(s, "auto", cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    /* continua */
                }
            }

            foreach (var s in BackgroundServices)
                await ProcessRunner.RunAsync("net.exe", $"start {s}", cancellationToken).ConfigureAwait(false);

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
