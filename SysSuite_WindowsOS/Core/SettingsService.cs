using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysSuite.Core
{
    /// <summary>
    /// Schema impostazioni (JSON in %AppData%\SysSuite\settings.json).
    /// Sezioni: navigazione/avvio, gaming, cronologia sessione, notifiche proattive
    /// (disco/CPU/RAM/driver), non disturbare, opzioni One-Click Boost.
    /// </summary>
    public class AppSettings
    {
        // ── Navigazione ──────────────────────────────────────────────
        [JsonPropertyName("lastTab")]
        public string LastNavTag { get; set; } = "Hub";

        /// <summary>Se true, all'avvio si riapre l'ultima scheda; se false, sempre Dashboard.</summary>
        [JsonPropertyName("restoreLastTabOnStartup")]
        public bool RestoreLastTabOnStartup { get; set; } = true;

        // ── Ottimizzazione ───────────────────────────────────────────
        [JsonPropertyName("lastOptTab")]
        public int LastOptimizationTab { get; set; } = 0;

        // ── Stato Gaming Mode ────────────────────────────────────────
        [JsonPropertyName("gamingModeActive")]
        public bool GamingModeActive { get; set; } = false;

        // ── Scansioni recenti ────────────────────────────────────────
        [JsonPropertyName("lastScanDate")]
        public DateTime? LastScanDate { get; set; }

        [JsonPropertyName("lastBoostDate")]
        public DateTime? LastBoostDate { get; set; }

        [JsonPropertyName("lastBoostMB")]
        public long LastBoostMB { get; set; } = 0;

        // ── Non disturbare (notifiche proattive disattivate fino a data/ora locale) ──
        [JsonPropertyName("dndUntil")]
        public DateTime? DndUntil { get; set; }

        // ── One-Click Boost (Dashboard) ───────────────────────────────
        [JsonPropertyName("boostCleanTemp")]
        public bool BoostCleanTemp { get; set; } = true;

        [JsonPropertyName("boostCleanThumbnails")]
        public bool BoostCleanThumbnails { get; set; } = true;

        [JsonPropertyName("boostCleanBrowserCache")]
        public bool BoostCleanBrowserCache { get; set; } = true;

        [JsonPropertyName("boostKillNotResponding")]
        public bool BoostKillNotResponding { get; set; } = true;

        // ── Notifiche ────────────────────────────────────────────────
        [JsonPropertyName("notifyDiskFull")]
        public bool NotifyDiskFull { get; set; } = true;

        [JsonPropertyName("notifyDriverProblems")]
        public bool NotifyDriverProblems { get; set; } = true;

        [JsonPropertyName("notifyHighCPU")]
        public bool NotifyHighCPU { get; set; } = true;

        [JsonPropertyName("notifyHighRAM")]
        public bool NotifyHighRAM { get; set; } = true;

        // ── Soglie notifiche ─────────────────────────────────────────
        [JsonPropertyName("diskWarningPct")]
        public double DiskWarningPct { get; set; } = 90.0;

        [JsonPropertyName("cpuWarningPct")]
        public double CpuWarningPct { get; set; } = 90.0;

        [JsonPropertyName("ramWarningPct")]
        public double RamWarningPct { get; set; } = 85.0;
    }

    public static class SettingsService
    {
        private static readonly string _folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysSuite");
        private static readonly string _file   = Path.Combine(_folder, "settings.json");
        private static readonly JsonSerializerOptions _opts =
            new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
        private static readonly object _lock = new();

        private static AppSettings? _cache;

        /// <summary>Carica le impostazioni dal file JSON. Crea il default se non esiste.</summary>
        public static AppSettings Load()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                try
                {
                    if (File.Exists(_file))
                    {
                        var json = File.ReadAllText(_file);
                        _cache = JsonSerializer.Deserialize<AppSettings>(json, _opts) ?? new AppSettings();
                        return _cache;
                    }
                }
                catch { }
                _cache = new AppSettings();
                return _cache;
            }
        }

        /// <summary>Salva le impostazioni su disco in modo sincrono.</summary>
        public static void Save(AppSettings settings)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(_folder);
                    File.WriteAllText(_file, JsonSerializer.Serialize(settings, _opts));
                    _cache = settings;
                }
                catch { /* silenzioso: la persistenza e' opzionale */ }
            }
        }

        /// <summary>Salva in modo asincrono — usare quando si e' sul thread UI.</summary>
        public static Task SaveAsync(AppSettings settings) =>
            Task.Run(() => Save(settings));

        /// <summary>Scorciatoia: aggiorna un campo e salva.</summary>
        public static void Update(Action<AppSettings> mutate)
        {
            var s = Load();
            mutate(s);
            Save(s);
        }

        /// <summary>Percorso file settings.json (solo lettura, per UI).</summary>
        public static string SettingsFilePath => _file;

        /// <summary>True se il non disturbare è attivo (ora locale prima della scadenza).</summary>
        public static bool IsDoNotDisturbActive()
        {
            var s = Load();
            return s.DndUntil.HasValue && s.DndUntil.Value > DateTime.Now;
        }
    }
}