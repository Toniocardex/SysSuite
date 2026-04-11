using Microsoft.Win32;
using SysSuite.Core;
using System.Threading;
using System.Threading.Tasks;

namespace SysSuite.Services
{
    /// <summary>
    /// Telemetria, Cortana, cronologia, pubblicità, privacy OS.
    /// Blocco telemetria a tre livelli: criteri di gruppo (AllowTelemetry), servizi DiagTrack / dmwappushservice,
    /// attività pianificate CEIP. Le API *Async eseguono il lavoro pesante senza bloccare la UI.
    /// </summary>
    public class PrivacyService
    {
        public event Action<string, string>? Log;

        // ── Telemetria (triple kill-switch) ─────────────────
        public bool IsTelemetryDisabled() =>
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") is int v && v == 0;

        public void SetTelemetry(bool enabled)
        {
            ApplyTelemetryFullAsync(enabled, default).GetAwaiter().GetResult();
            Emit(enabled ? "Telemetria riabilitata" : "Telemetria disabilitata", "ok");
        }

        public async Task SetTelemetryAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => ApplyTelemetryFullAsync(enabled, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            Emit(enabled ? "Telemetria riabilitata" : "Telemetria disabilitata", "ok");
        }

        /// <summary>
        /// 1) AllowTelemetry (0 off, 3 on), 2) sc stop/config servizi telemetria, 3) schtasks CEIP.
        /// Ogni passo è isolato: errori → Serilog e si prosegue.
        /// </summary>
        private static async Task ApplyTelemetryFullAsync(bool enabled, CancellationToken cancellationToken)
        {
            try
            {
                ApplyTelemetryRegistry(enabled);
            }
            catch (Exception ex)
            {
                global::Serilog.Log.Warning(ex, "Telemetria: impostazione registro AllowTelemetry non riuscita");
            }

            await ApplyTelemetryServicesAsync(enabled, cancellationToken).ConfigureAwait(false);
            await ApplyTelemetryScheduledTasksAsync(enabled, cancellationToken).ConfigureAwait(false);
        }

        private static void ApplyTelemetryRegistry(bool enabled) =>
            SetReg(Registry.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry", enabled ? 3 : 0);

        private static async Task ApplyTelemetryServicesAsync(bool enabled, CancellationToken cancellationToken)
        {
            if (enabled)
            {
                await TryTelemetryProcessAsync("sc.exe", "config DiagTrack start= auto", cancellationToken,
                    "DiagTrack config auto").ConfigureAwait(false);
                await TryTelemetryProcessAsync("sc.exe", "start DiagTrack", cancellationToken,
                    "DiagTrack start").ConfigureAwait(false);
                await TryTelemetryProcessAsync("sc.exe", "config dmwappushservice start= auto", cancellationToken,
                    "dmwappushservice config auto").ConfigureAwait(false);
                await TryTelemetryProcessAsync("sc.exe", "start dmwappushservice", cancellationToken,
                    "dmwappushservice start").ConfigureAwait(false);
            }
            else
            {
                await TryTelemetryProcessAsync("sc.exe", "config DiagTrack start= disabled", cancellationToken,
                    "DiagTrack config disabled").ConfigureAwait(false);
                await TryTelemetryProcessAsync("sc.exe", "stop DiagTrack", cancellationToken,
                    "DiagTrack stop").ConfigureAwait(false);
                await TryTelemetryProcessAsync("sc.exe", "config dmwappushservice start= disabled", cancellationToken,
                    "dmwappushservice config disabled").ConfigureAwait(false);
                await TryTelemetryProcessAsync("sc.exe", "stop dmwappushservice", cancellationToken,
                    "dmwappushservice stop").ConfigureAwait(false);
            }
        }

        private static async Task ApplyTelemetryScheduledTasksAsync(bool enabled, CancellationToken cancellationToken)
        {
            string flag = enabled ? "/enable" : "/disable";
            string[] taskPaths =
            {
                @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"
            };

            foreach (var tn in taskPaths)
            {
                string args = "/change /tn \"" + tn + "\" " + flag;
                await TryTelemetryProcessAsync("schtasks.exe", args, cancellationToken,
                    "Attività pianificata " + tn).ConfigureAwait(false);
            }
        }

        private static async Task TryTelemetryProcessAsync(string fileName, string arguments,
            CancellationToken cancellationToken, string stepDescription)
        {
            try
            {
                await ProcessRunner.RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                global::Serilog.Log.Warning(ex, "Telemetria: passo non riuscito ({Step}): {File} {Args}", stepDescription,
                    fileName, arguments);
            }
        }

        // ── ID Pubblicità ───────────────────────────────────
        public bool IsAdvertisingIdDisabled() =>
            ReadReg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") is int v && v == 0;

        public void SetAdvertisingId(bool enabled)
        {
            ApplyAdvertisingId(enabled);
            Emit(enabled ? "ID pubblicità riabilitato" : "ID pubblicità disabilitato", "ok");
        }

        public async Task SetAdvertisingIdAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => ApplyAdvertisingId(enabled), cancellationToken).ConfigureAwait(false);
            Emit(enabled ? "ID pubblicità riabilitato" : "ID pubblicità disabilitato", "ok");
        }

        private static void ApplyAdvertisingId(bool enabled) =>
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                "Enabled", enabled ? 1 : 0);

        // ── Cronologia attività ─────────────────────────────
        public bool IsActivityHistoryDisabled() =>
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed") is int v && v == 0;

        public void SetActivityHistory(bool enabled)
        {
            ApplyActivityHistory(enabled);
            Emit(enabled ? "Cronologia attività riabilitata" : "Cronologia attività disabilitata", "ok");
        }

        public async Task SetActivityHistoryAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => ApplyActivityHistory(enabled), cancellationToken).ConfigureAwait(false);
            Emit(enabled ? "Cronologia attività riabilitata" : "Cronologia attività disabilitata", "ok");
        }

        private static void ApplyActivityHistory(bool enabled) =>
            SetReg(Registry.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System",
                "EnableActivityFeed", enabled ? 1 : 0);

        // ── Suggerimenti Start ───────────────────────────────
        public bool IsStartSuggestionsDisabled() =>
            ReadReg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled") is int v && v == 0;

        public void SetStartSuggestions(bool enabled)
        {
            ApplyStartSuggestions(enabled);
            Emit(enabled ? "Suggerimenti Start riabilitati" : "Suggerimenti Start disabilitati", "ok");
        }

        public async Task SetStartSuggestionsAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => ApplyStartSuggestions(enabled), cancellationToken).ConfigureAwait(false);
            Emit(enabled ? "Suggerimenti Start riabilitati" : "Suggerimenti Start disabilitati", "ok");
        }

        private static void ApplyStartSuggestions(bool enabled) =>
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SystemPaneSuggestionsEnabled", enabled ? 1 : 0);

        // ── Cortana ──────────────────────────────────────────
        public bool IsCortanaDisabled() =>
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana") is int v && v == 0;

        public void SetCortana(bool enabled)
        {
            ApplyCortana(enabled);
            Emit(enabled ? "Cortana riabilitata" : "Cortana disabilitata", "ok");
        }

        public async Task SetCortanaAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => ApplyCortana(enabled), cancellationToken).ConfigureAwait(false);
            Emit(enabled ? "Cortana riabilitata" : "Cortana disabilitata", "ok");
        }

        private static void ApplyCortana(bool enabled) =>
            SetReg(Registry.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                "AllowCortana", enabled ? 1 : 0);

        // ── Suggerimenti schermata di blocco ──────────────────
        public bool IsLockScreenTipsDisabled() =>
            ReadReg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled") is int v && v == 0;

        public void SetLockScreenTips(bool enabled)
        {
            ApplyLockScreenTips(enabled);
            Emit(enabled ? "Suggerimenti blocco schermo riabilitati" : "Suggerimenti blocco schermo disabilitati", "ok");
        }

        public async Task SetLockScreenTipsAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => ApplyLockScreenTips(enabled), cancellationToken).ConfigureAwait(false);
            Emit(enabled ? "Suggerimenti blocco schermo riabilitati" : "Suggerimenti blocco schermo disabilitati", "ok");
        }

        private static void ApplyLockScreenTips(bool enabled) =>
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "RotatingLockScreenOverlayEnabled", enabled ? 1 : 0);

        // ── Helpers ──────────────────────────────────────────
        private static void SetReg(RegistryKey root, string path, string name, object value)
        {
            using var key = root.CreateSubKey(path, true);
            key?.SetValue(name, value);
        }

        private static object? ReadReg(RegistryKey root, string path, string name)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                return key?.GetValue(name);
            }
            catch { return null; }
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}
