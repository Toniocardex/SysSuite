using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using SysSuite.Interop;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>
    /// Servizi Windows: enumerazione nativa (<see cref="NtServices.EnumServicesStatusExW"/> + buffer riusabile),
    /// modifiche con <c>sc.exe</c> / <see cref="ServiceController"/>; paracadute WMI prima di Stop/Disable.
    /// </summary>
    public sealed class ServicesManager
    {
        private readonly SystemRestoreService? _systemRestore;

        /// <summary>Buffer SCM riusabile (max 256 KiB per contratto Win32).</summary>
        private nint _enumScratchBuffer;
        private uint _enumScratchSize;

        private Dictionary<string, uint>? _lastStateByName;

        public event Action<string, string>? Log;

        public ServicesManager(SystemRestoreService? systemRestore = null)
        {
            _systemRestore = systemRestore;
        }

        ~ServicesManager()
        {
            unsafe
            {
                if (_enumScratchBuffer != 0)
                {
                    NativeMemory.Free((void*)_enumScratchBuffer);
                    _enumScratchBuffer = 0;
                }
            }
        }

        // Servizi sicuri da disabilitare con descrizione
        public static readonly Dictionary<string, string> SafeToDisable = new()
        {
            ["Fax"] = "Servizio fax — inutile senza fax fisico",
            ["RemoteRegistry"] = "Accesso remoto al registro — rischio sicurezza",
            ["DiagTrack"] = "Telemetria Microsoft — invia dati a Microsoft",
            ["lfsvc"] = "Geolocalizzazione",
            ["XblAuthManager"] = "Xbox Live Auth — inutile senza Xbox",
            ["XblGameSave"] = "Xbox Game Save",
            ["XboxNetApiSvc"] = "Xbox Network",
            ["XboxGipSvc"] = "Xbox Input",
            ["WSearch"] = "Windows Search (usa CPU, utile solo su HDD)",
        };

        /// <summary>
        /// Enumerazione completa tramite <c>EnumServicesStatusExW</c> (<see cref="NtServices.ScEnumProcessInfo"/>),
        /// parsing con <see cref="ReadOnlySpan{T}"/> / <see cref="MemoryMarshal"/>; tipo avvio da registro.
        /// </summary>
        public unsafe IReadOnlyList<WindowsServiceListItem> EnumerateAllServicesNative()
        {
            Dictionary<string, uint> startMap = BuildServiceStartTypeMap();

            nint hMgr = NtServices.OpenSCManager(IntPtr.Zero, IntPtr.Zero, NtServices.ScManagerEnumerateService);
            if (hMgr == 0)
                throw new InvalidOperationException("OpenSCManager fallita: " + Marshal.GetLastWin32Error());

            uint resume = 0;
            _ = NtServices.EnumServicesStatusExW(
                hMgr,
                NtServices.ScEnumProcessInfo,
                NtServices.ServiceTypeWin32 | NtServices.ServiceTypeDriver,
                NtServices.ServiceStateAll,
                null,
                0,
                out uint bytesNeeded,
                out uint _,
                ref resume,
                IntPtr.Zero);

            int probeErr = Marshal.GetLastWin32Error();
            if (probeErr != 0 && probeErr != (int)NtServices.ErrorMoreData)
            {
                _ = NtServices.CloseServiceHandle(hMgr);
                throw new InvalidOperationException("EnumServicesStatusExW (probe): " + probeErr);
            }

            uint alloc = Math.Min(Math.Max(bytesNeeded + 4096, 64 * 1024), NtServices.MaxEnumBufferBytes);
            EnsureEnumScratchBuffer(alloc);

            var list = new List<WindowsServiceListItem>(512);
            var stateMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            int stride = Unsafe.SizeOf<NtServices.EnumServiceStatusProcess>();
            byte* scratch = (byte*)_enumScratchBuffer;

            try
            {
                resume = 0;
                while (true)
                {
                    bool ok = NtServices.EnumServicesStatusExW(
                        hMgr,
                        NtServices.ScEnumProcessInfo,
                        NtServices.ServiceTypeWin32 | NtServices.ServiceTypeDriver,
                        NtServices.ServiceStateAll,
                        scratch,
                        _enumScratchSize,
                        out bytesNeeded,
                        out uint returned,
                        ref resume,
                        IntPtr.Zero);

                    int err = Marshal.GetLastWin32Error();
                    if (!ok && err != 0 && err != (int)NtServices.ErrorMoreData)
                        throw new InvalidOperationException("EnumServicesStatusExW: " + err);

                    if (returned == 0 && resume == 0)
                        break;

                    int structBytes = checked((int)(returned * (uint)stride));
                    ReadOnlySpan<byte> blob = new(scratch, structBytes);
                    for (int i = 0; i < returned; i++)
                    {
                        ReadOnlySpan<byte> slice = blob.Slice(i * stride, stride);
                        ref readonly NtServices.EnumServiceStatusProcess row =
                            ref MemoryMarshal.AsRef<NtServices.EnumServiceStatusProcess>(slice);

                        string name = Marshal.PtrToStringUni(row.lpServiceName) ?? "";
                        if (name.Length == 0)
                            continue;
                        string display = Marshal.PtrToStringUni(row.lpDisplayName) ?? "";
                        uint state = row.ServiceStatusProcess.dwCurrentState;
                        uint pid = row.ServiceStatusProcess.dwProcessId;
                        stateMap[name] = state;
                        uint start = startMap.GetValueOrDefault(name, 99u);

                        list.Add(new WindowsServiceListItem
                        {
                            Name = name,
                            DisplayName = display,
                            CurrentState = state,
                            StateText = MapServiceState(state),
                            StartType = start,
                            StartTypeText = MapStartType(start),
                            ProcessId = pid
                        });
                    }

                    if (resume == 0)
                        break;
                }
            }
            finally
            {
                _ = NtServices.CloseServiceHandle(hMgr);
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _lastStateByName = stateMap;
            return new ReadOnlyCollection<WindowsServiceListItem>(list);
        }

        private void EnsureEnumScratchBuffer(uint minBytes)
        {
            unsafe
            {
                if (_enumScratchBuffer != 0 && _enumScratchSize >= minBytes)
                    return;

                if (_enumScratchBuffer != 0)
                {
                    NativeMemory.Free((void*)_enumScratchBuffer);
                    _enumScratchBuffer = 0;
                }

                _enumScratchSize = Math.Max(minBytes, 64 * 1024);
                _enumScratchSize = Math.Min(_enumScratchSize, NtServices.MaxEnumBufferBytes);
                _enumScratchBuffer = (nint)NativeMemory.Alloc(_enumScratchSize);
            }
        }

        private static Dictionary<string, uint> BuildServiceStartTypeMap()
        {
            var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using RegistryKey? root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (root == null)
                    return d;
                foreach (string name in root.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? sk = root.OpenSubKey(name);
                        object? o = sk?.GetValue("Start");
                        if (o is int st)
                            d[name] = (uint)st;
                    }
                    catch
                    {
                        /* voce illeggibile */
                    }
                }
            }
            catch
            {
                /* hive non disponibile */
            }

            return d;
        }

        private static string MapServiceState(uint s) =>
            s switch
            {
                1 => "Fermato",
                2 => "Avvio in corso",
                3 => "Arresto in corso",
                4 => "In esecuzione",
                5 => "Ripresa in corso",
                6 => "Pausa in corso",
                7 => "In pausa",
                _ => "Stato (" + s + ")"
            };

        private static string MapStartType(uint s) =>
            s switch
            {
                0 => "Avvio boot",
                1 => "Avvio sistema",
                2 => "Automatico",
                3 => "Manuale",
                4 => "Disabilitato",
                _ => "n/d"
            };

        public async Task DisableAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await TryCreateRestorePointBeforeStopOrDisableAsync(serviceName, "Disable", cancellationToken)
                .ConfigureAwait(false);

            try
            {
                using var svc = new ServiceController(serviceName);
                if (svc.Status != ServiceControllerStatus.Stopped)
                    svc.Stop();
                SetStartMode(serviceName, "disabled");
                Emit($"Disabilitato: {serviceName}", "ok");
            }
            catch (Exception ex)
            {
                Emit($"Errore {serviceName}: {ex.Message}", "warn");
            }
        }

        public Task EnableAsync(string serviceName, string startMode = "auto",
            CancellationToken cancellationToken = default)
        {
            try
            {
                SetStartMode(serviceName, startMode);
                using var svc = new ServiceController(serviceName);
                if (svc.Status != ServiceControllerStatus.Running)
                    svc.Start();
                Emit($"Abilitato: {serviceName}", "ok");
            }
            catch (Exception ex)
            {
                Emit($"Errore {serviceName}: {ex.Message}", "warn");
            }

            return Task.CompletedTask;
        }

        public async Task RestartAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await DisableAsync(serviceName, cancellationToken).ConfigureAwait(false);
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            await EnableAsync(serviceName, "auto", cancellationToken).ConfigureAwait(false);
        }

        public ServiceControllerStatus GetStatus(string serviceName)
        {
            if (_lastStateByName != null
                && _lastStateByName.TryGetValue(serviceName, out uint st)
                && st is >= 1 and <= 7)
                return (ServiceControllerStatus)st;

            try
            {
                using var svc = new ServiceController(serviceName);
                return svc.Status;
            }
            catch
            {
                return ServiceControllerStatus.Stopped;
            }
        }

        private async Task TryCreateRestorePointBeforeStopOrDisableAsync(
            string serviceName,
            string operationLabel,
            CancellationToken cancellationToken)
        {
            if (_systemRestore == null)
                return;

            try
            {
                (bool on, _) = await _systemRestore
                    .IsSystemRestoreEnabledAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!on)
                    return;

                string desc = "SysSuite Checkpoint: Servizio " + operationLabel + " - " + serviceName;
                (bool ok, uint rv, string? err) = await _systemRestore
                    .CreateRestorePointAsync(desc, cancellationToken)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    Emit(
                        $"Punto di ripristino non creato (ReturnValue={rv}): {err ?? "sconosciuto"}. Procedo con {operationLabel} su {serviceName}.",
                        "warn");
                }
            }
            catch (Exception ex)
            {
                Emit($"Ripristino (pre-{operationLabel}): {ex.Message}. Procedo comunque.", "warn");
            }
        }

        private static void SetStartMode(string name, string mode)
        {
            using var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("sc.exe", $"config {name} start= {mode}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            p.Start();
            p.WaitForExit();
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}
