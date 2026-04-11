using Microsoft.Win32;

namespace SysSuite.Services
{
    /// <summary>Telemetria, Cortana, cronologia, pubblicità, privacy OS. Ogni impostazione può essere letta, disabilitata e ripristinata.</summary>
    public class PrivacyService
    {
        public event Action<string,string>? Log;

        // ── Telemetria ──────────────────────────────────────
        public bool IsTelemetryDisabled() =>
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") is int v && v == 0;

        public void SetTelemetry(bool enabled)
        {
            SetReg(Registry.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry", enabled ? 1 : 0);
            Emit(enabled ? "Telemetria riabilitata" : "Telemetria disabilitata", "ok");
        }

        // ── ID Pubblicità ───────────────────────────────────
        public bool IsAdvertisingIdDisabled() =>
            ReadReg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") is int v && v == 0;

        public void SetAdvertisingId(bool enabled)
        {
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                "Enabled", enabled ? 1 : 0);
            Emit(enabled ? "ID pubblicità riabilitato" : "ID pubblicità disabilitato", "ok");
        }

        // ── Cronologia attività ─────────────────────────────
        public bool IsActivityHistoryDisabled() =>
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed") is int v && v == 0;

        public void SetActivityHistory(bool enabled)
        {
            SetReg(Registry.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System",
                "EnableActivityFeed", enabled ? 1 : 0);
            Emit(enabled ? "Cronologia attività riabilitata" : "Cronologia attività disabilitata", "ok");
        }

        // ── Suggerimenti Start ───────────────────────────────
        public bool IsStartSuggestionsDisabled() =>
            ReadReg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled") is int v && v == 0;

        public void SetStartSuggestions(bool enabled)
        {
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SystemPaneSuggestionsEnabled", enabled ? 1 : 0);
            Emit(enabled ? "Suggerimenti Start riabilitati" : "Suggerimenti Start disabilitati", "ok");
        }

        // ── Cortana ──────────────────────────────────────────
        public bool IsCortanaDisabled() =>
            ReadReg(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana") is int v && v == 0;

        public void SetCortana(bool enabled)
        {
            SetReg(Registry.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                "AllowCortana", enabled ? 1 : 0);
            Emit(enabled ? "Cortana riabilitata" : "Cortana disabilitata", "ok");
        }

        // ── Suggerimenti schermata di blocco ──────────────────
        public bool IsLockScreenTipsDisabled() =>
            ReadReg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled") is int v && v == 0;

        public void SetLockScreenTips(bool enabled)
        {
            SetReg(Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "RotatingLockScreenOverlayEnabled", enabled ? 1 : 0);
            Emit(enabled ? "Suggerimenti blocco schermo riabilitati" : "Suggerimenti blocco schermo disabilitati", "ok");
        }

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
