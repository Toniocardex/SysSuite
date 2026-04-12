using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SysSuite.Core;

namespace SysSuite.Services
{
    /// <summary>
    /// Network Booster (gaming): punto di ripristino, tweak TCP/IP su interfacce,
    /// MSMQ <c>TCPNoDelay</c>, flush DNS silenzioso.
    /// </summary>
    public sealed class NetworkOptimizationService
    {
        private readonly SystemRestoreService _systemRestore;

        private const string TcpInterfacesRoot =
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        private const string MsmqParametersPath = @"SOFTWARE\Microsoft\MSMQ\Parameters";

        public event Action<string, string>? Log;

        public NetworkOptimizationService(SystemRestoreService systemRestore) =>
            _systemRestore = systemRestore;

        private void Emit(string msg, string level) => Log?.Invoke(msg, level);

        /// <summary>
        /// Ordine: ripristino WMI, registro interfacce + MSMQ, <c>ipconfig /flushdns</c>.
        /// </summary>
        public async Task OptimizeForGamingAsync(CancellationToken cancellationToken = default)
        {
            (bool Success, uint ReturnValue, string? ErrorMessage) rp =
                await _systemRestore.CreateRestorePointAsync(
                    "SysSuite Network Optimization",
                    cancellationToken).ConfigureAwait(false);

            if (rp.Success)
                Emit("Punto di ripristino creato (SysSuite Network Optimization).", "ok");
            else
                Emit(
                    "Punto di ripristino non creato: "
                    + (rp.ErrorMessage ?? "WMI " + rp.ReturnValue)
                    + ". Procedo con le modifiche al registro.",
                    "warn");

            await Task.Run(
                    () => ApplyRegistryOptimizations(cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            await ProcessRunner.RunAsync("ipconfig.exe", "/flushdns", cancellationToken)
                .ConfigureAwait(false);
            Emit("Cache DNS svuotata (Network Booster).", "ok");

            Emit(
                "Network Booster: TcpAckFrequency, TCPNoDelay, TcpDelAckTicks e MSMQ TCPNoDelay applicati.",
                "ok");
        }

        private void ApplyRegistryOptimizations(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(TcpInterfacesRoot, writable: false);
            if (baseKey == null)
            {
                Emit("Chiave Tcpip\\Parameters\\Interfaces non accessibile.", "err");
                throw new InvalidOperationException(
                    "Impossibile aprire HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces.");
            }

            foreach (string sub in baseKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using RegistryKey? iface = Registry.LocalMachine.OpenSubKey(
                    TcpInterfacesRoot + "\\" + sub,
                    writable: true);
                if (iface == null)
                    continue;

                iface.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                iface.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                iface.SetValue("TcpDelAckTicks", 0, RegistryValueKind.DWord);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using RegistryKey? msmq = Registry.LocalMachine.CreateSubKey(
                MsmqParametersPath,
                writable: true);
            if (msmq == null)
            {
                Emit("Impossibile creare/aprire HKLM\\SOFTWARE\\Microsoft\\MSMQ\\Parameters.", "err");
                throw new InvalidOperationException("Chiave MSMQ Parameters non disponibile.");
            }

            msmq.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
        }
    }
}
