using System.Management;
using System.Threading;
using System.Threading.Tasks;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>
    /// Ripristino configurazione di sistema tramite WMI (<c>root\default:SystemRestore</c> e <c>StdRegProv</c>).
    /// Le chiamate WMI sono eseguite in <see cref="Task.Run"/> per non bloccare il thread chiamante (es. UI).
    /// </summary>
    public sealed class SystemRestoreService
    {
        /// <summary>EventType WMI: inizio modifica di sistema.</summary>
        private const int EventTypeBeginSystemChange = 100;

        /// <summary>RestorePointType WMI: installazione applicazione (come da direttiva).</summary>
        private const int RestorePointTypeApplicationInstall = 0;

        private const uint HkeyLocalMachine = 0x80000002;

        public event Action<string, string>? Log;

        /// <summary>
        /// Verifica se il ripristino di configurazione risulta attivo a livello di sistema (tipicamente volume di sistema C:).
        /// Usa <c>SystemRestoreConfig</c> e, in <c>root\default</c>, <c>StdRegProv.GetDWORDValue</c> per <c>DisableSR</c>.
        /// </summary>
        public Task<(bool Enabled, string? ErrorMessage)> IsSystemRestoreEnabledAsync(
            CancellationToken cancellationToken = default) =>
            Task.Run(IsSystemRestoreEnabledCore, cancellationToken);

        /// <summary>Crea un punto di ripristino con la descrizione indicata (WMI <c>CreateRestorePoint</c>).</summary>
        public Task<(bool Success, uint ReturnValue, string? ErrorMessage)> CreateRestorePointAsync(
            string description,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => CreateRestorePointCore(description), cancellationToken);

        /// <summary>Elenca i punti di ripristino (proprietà Description, SequenceNumber, CreationTime).</summary>
        public Task<(IReadOnlyList<SystemRestorePointInfo> Points, string? ErrorMessage)> GetRestorePointsAsync(
            CancellationToken cancellationToken = default) =>
            Task.Run(GetRestorePointsCore, cancellationToken);

        private (bool Enabled, string? ErrorMessage) IsSystemRestoreEnabledCore()
        {
            try
            {
                bool globalActive = false;
                using (var searcher = new ManagementObjectSearcher(
                           new ManagementScope(@"root\default"),
                           new ObjectQuery("SELECT * FROM SystemRestoreConfig")))
                {
                    using ManagementObjectCollection col = searcher.Get();
                    foreach (ManagementObject mo in col)
                    {
                        using (mo)
                        {
                            uint diskPercent = ToUInt(mo["DiskPercent"]);
                            uint rpGlobal = ToUInt(mo["RPGlobalInterval"]);
                            globalActive = diskPercent > 0 || rpGlobal > 0;
                            Emit(
                                globalActive
                                    ? $"Ripristino: configurazione attiva (DiskPercent={diskPercent}, RPGlobalInterval={rpGlobal})."
                                    : "Ripristino: configurazione globale non attiva (DiskPercent e RPGlobalInterval a zero).",
                                globalActive ? "ok" : "warn");
                            break;
                        }
                    }
                }

                if (!globalActive)
                    return (false, "System Restore non attivo (WMI SystemRestoreConfig).");

                if (IsDisableSrSetViaStdRegProv())
                {
                    Emit("Ripristino disattivato da DisableSR (StdRegProv / HKLM).", "warn");
                    return (false, "System Restore disattivato (valore DisableSR).");
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                Emit($"IsSystemRestoreEnabled: {ex.Message}", "err");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Legge <c>HKLM\...\SystemRestore\DisableSR</c> tramite WMI <c>root\default:StdRegProv</c> (nessun Microsoft.Win32.Registry).
        /// </summary>
        private bool IsDisableSrSetViaStdRegProv()
        {
            try
            {
                var scope = new ManagementScope(@"root\default");
                scope.Connect();
                using var stdReg = new ManagementClass(scope, new ManagementPath("StdRegProv"), new ObjectGetOptions());
                using ManagementBaseObject? inParams = stdReg.GetMethodParameters("GetDWORDValue");
                if (inParams == null)
                    return false;

                inParams["hDefKey"] = HkeyLocalMachine;
                inParams["sSubKeyName"] = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
                inParams["sValueName"] = "DisableSR";

                using ManagementBaseObject? outParams = stdReg.InvokeMethod("GetDWORDValue", inParams, null);
                if (outParams == null)
                    return false;

                object? rv = outParams.Properties["ReturnValue"]?.Value;
                uint ret = rv == null ? 2 : Convert.ToUInt32(rv);
                if (ret != 0)
                    return false;
                uint uValue = Convert.ToUInt32(outParams.Properties["uValue"]?.Value ?? 0u);
                return uValue == 1;
            }
            catch
            {
                return false;
            }
        }

        private (bool Success, uint ReturnValue, string? ErrorMessage) CreateRestorePointCore(string description)
        {
            string desc = SanitizeDescription(description);
            try
            {
                var scope = new ManagementScope(@"root\default");
                scope.Connect();

                using var sysRestore = new ManagementClass(
                    scope,
                    new ManagementPath("SystemRestore"),
                    new ObjectGetOptions());

                using (ManagementBaseObject? inParams = sysRestore.GetMethodParameters("CreateRestorePoint"))
                {
                    if (inParams == null)
                    {
                        const string msg = "Metodo CreateRestorePoint non disponibile.";
                        Emit(msg, "err");
                        return (false, uint.MaxValue, msg);
                    }

                    inParams["Description"] = desc;
                    inParams["RestorePointType"] = RestorePointTypeApplicationInstall;
                    inParams["EventType"] = EventTypeBeginSystemChange;

                    var invokeOptions = new InvokeMethodOptions { Timeout = TimeSpan.FromMinutes(6) };

                    using ManagementBaseObject? outParams = sysRestore.InvokeMethod(
                        "CreateRestorePoint",
                        inParams,
                        invokeOptions);

                    if (outParams == null)
                    {
                        const string msg = "CreateRestorePoint: nessun output WMI.";
                        Emit(msg, "err");
                        return (false, uint.MaxValue, msg);
                    }

                    object? rvObj = outParams.Properties["ReturnValue"]?.Value;
                    uint returnValue = rvObj == null ? 0 : Convert.ToUInt32(rvObj);
                    if (returnValue == 0)
                    {
                        Emit($"Punto di ripristino creato: \"{desc}\"", "ok");
                        return (true, 0, null);
                    }

                    string err = $"CreateRestorePoint fallito (ReturnValue={returnValue}).";
                    Emit(err, "warn");
                    return (false, returnValue, err);
                }
            }
            catch (Exception ex)
            {
                Emit($"CreateRestorePoint: {ex.Message}", "err");
                return (false, uint.MaxValue, ex.Message);
            }
        }

        private static string SanitizeDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "SysSuite_Checkpoint";
            string d = description.Trim();
            ReadOnlySpan<char> span = d.AsSpan();
            Span<char> buf = stackalloc char[Math.Min(span.Length, 240)];
            int n = 0;
            for (int i = 0; i < span.Length && n < buf.Length; i++)
            {
                char c = span[i];
                buf[n++] = char.IsControl(c) ? ' ' : c;
            }

            return new string(buf[..n]).Trim();
        }

        private (IReadOnlyList<SystemRestorePointInfo> Points, string? ErrorMessage) GetRestorePointsCore()
        {
            try
            {
                var scope = new ManagementScope(@"root\default");
                scope.Connect();

                using var sysRestore = new ManagementClass(
                    scope,
                    new ManagementPath("SystemRestore"),
                    new ObjectGetOptions());

                var list = new List<SystemRestorePointInfo>();
                foreach (ManagementObject mo in sysRestore.GetInstances())
                {
                    using (mo)
                    {
                        list.Add(new SystemRestorePointInfo
                        {
                            SequenceNumber = ToUInt(mo["SequenceNumber"]),
                            Description = mo["Description"]?.ToString() ?? "",
                            RestorePointType = ToUInt(mo["RestorePointType"]),
                            EventType = ToUInt(mo["EventType"]),
                            CreationTimeRaw = mo["CreationTime"]?.ToString() ?? "",
                            CreationTimeDisplay = FormatWmiDateTime(mo["CreationTime"]?.ToString())
                        });
                    }
                }

                Emit($"Elenco punti di ripristino: {list.Count} elementi.", "ok");
                return (list, null);
            }
            catch (Exception ex)
            {
                Emit($"GetRestorePoints: {ex.Message}", "err");
                return (Array.Empty<SystemRestorePointInfo>(), ex.Message);
            }
        }

        private static uint ToUInt(object? value) =>
            value == null ? 0 : Convert.ToUInt32(value);

        private static string FormatWmiDateTime(string? raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length < 14) return "";
            try
            {
                int y = int.Parse(raw.AsSpan(0, 4));
                int m = int.Parse(raw.AsSpan(4, 2));
                int d = int.Parse(raw.AsSpan(6, 2));
                int h = int.Parse(raw.AsSpan(8, 2));
                int min = int.Parse(raw.AsSpan(10, 2));
                int s = int.Parse(raw.AsSpan(12, 2));
                return new DateTime(y, m, d, h, min, s).ToString("g");
            }
            catch
            {
                return "";
            }
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}
