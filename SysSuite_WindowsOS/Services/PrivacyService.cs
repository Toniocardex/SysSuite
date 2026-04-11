using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;

namespace SysSuite.Services
{
    /// <summary>
    /// Telemetria, Cortana, cronologia, pubblicità, privacy OS.
    /// Non avvia processi esterni: solo scritture su registro. Le API *Async eseguono le mutazioni
    /// su thread pool così la UI resta reattiva (HKLM può essere lento sotto carico).
    /// </summary>
    public class PrivacyService
    {
        public event Action<string, string>? Log;

        // ── Telemetria ──────────────────────────────────────
        public bool IsTelemetryDisabled() =>
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") is int v && v == 0;

        public void SetTelemetry(bool enabled)
        {
            ApplyTelemetry(enabled);
            Emit(enabled ? "Telemetria riabilitata" : "Telemetria disabilitata", "ok");
        }

        public async Task SetTelemetryAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => ApplyTelemetry(enabled), cancellationToken).ConfigureAwait(false);
            Emit(enabled ? "Telemetria riabilitata" : "Telemetria disabilitata", "ok");
        }

        private static void ApplyTelemetry(bool enabled) =>
            SetReg(Registry.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry", enabled ? 1 : 0);

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
