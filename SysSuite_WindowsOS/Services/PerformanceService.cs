using SysSuite.Core;
using Microsoft.Win32;

namespace SysSuite.Services
{
    /// <summary>Piani alimentazione, animazioni, avvio rapido, defrag.</summary>
    public class PerformanceService
    {
        public event Action<string,string>? Log;

        private const string BalancedGuid  = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string UltimateGuid  = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        public void SetBalancedPlan()
        {
            ProcessRunner.Run("powercfg.exe", $"/setactive {BalancedGuid}");
            Emit("Piano bilanciato attivato", "ok");
        }

        public void SetUltimatePlan()
        {
            ProcessRunner.Run("powercfg.exe", $"/duplicatescheme {UltimateGuid}");
            ProcessRunner.Run("powercfg.exe", $"/setactive {UltimateGuid}");
            Emit("Piano Ultimate Performance attivato", "ok");
        }

        public string GetCurrentPlan()
        {
            var p = new System.Diagnostics.Process
            {
                StartInfo = new("powercfg.exe", "/getactivescheme")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow  = true
                }
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var m = System.Text.RegularExpressions.Regex.Match(output, @"\((.+)\)");
            return m.Success ? m.Groups[1].Value : "Sconosciuto";
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

        public void OptimizeDisk(string drive = "C:")
        {
            Emit($"Ottimizzazione disco {drive}...", "head");
            ProcessRunner.Run("defrag.exe", $"{drive} /O");
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