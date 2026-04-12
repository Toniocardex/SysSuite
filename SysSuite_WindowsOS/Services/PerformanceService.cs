using SysSuite.Core;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;

namespace SysSuite.Services
{
    /// <summary>Piani alimentazione, animazioni, avvio rapido, defrag.</summary>
    public class PerformanceService
    {
        public event Action<string,string>? Log;

        private const string BalancedGuid  = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string UltimateGuid  = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        public async Task SetBalancedPlanAsync(CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAsync("powercfg.exe", $"/setactive {BalancedGuid}", cancellationToken).ConfigureAwait(false);
            Emit("Piano bilanciato attivato", "ok");
        }

        public async Task SetUltimatePlanAsync(CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAsync("powercfg.exe", $"/duplicatescheme {UltimateGuid}", cancellationToken).ConfigureAwait(false);
            await ProcessRunner.RunAsync("powercfg.exe", $"/setactive {UltimateGuid}", cancellationToken).ConfigureAwait(false);
            Emit("Piano Ultimate Performance attivato", "ok");
        }

        public string GetCurrentPlan()
        {
            var (_, output) = ProcessRunner.RunCapture("powercfg.exe", "/getactivescheme");
            var m = System.Text.RegularExpressions.Regex.Match(output, @"\((.+)\)");
            return m.Success ? m.Groups[1].Value : "Sconosciuto";
        }

        public async Task<string> GetCurrentPlanAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(TimeSpan.FromSeconds(10));
                var (_, output) = await ProcessRunner.RunCaptureAsync("powercfg.exe", "/getactivescheme", linked.Token).ConfigureAwait(false);
                var m = System.Text.RegularExpressions.Regex.Match(output, @"\((.+)\)");
                return m.Success ? m.Groups[1].Value : "Sconosciuto";
            }
            catch (OperationCanceledException)
            {
                return "n/d (timeout powercfg)";
            }
        }

        public void ReduceAnimations()
        {
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                "VisualFXSetting", 2);
            SetReg(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0");
            Emit("Animazioni ridotte", "ok");
        }

        public void RestoreAnimations()
        {
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                "VisualFXSetting", 0);
            Emit("Animazioni ripristinate", "ok");
        }

        public void EnableFastStartup()
        {
            SetReg(Registry.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                "HiberbootEnabled", 1);
            Emit("Avvio rapido abilitato", "ok");
        }

        public async Task OptimizeDiskAsync(string drive = "C:", CancellationToken cancellationToken = default)
        {
            Emit($"Ottimizzazione disco {drive}...", "head");
            await ProcessRunner.RunAsync("defrag.exe", $"{drive} /O", cancellationToken).ConfigureAwait(false);
            Emit("Disco ottimizzato", "ok");
        }


        private static void SetReg(RegistryKey root, string path, string name, object value)
        {
            using var key = root.CreateSubKey(path, true);
            key?.SetValue(name, value);
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}
