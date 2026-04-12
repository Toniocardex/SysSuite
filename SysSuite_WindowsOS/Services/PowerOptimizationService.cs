using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SysSuite.Core;

namespace SysSuite.Services
{
    /// <summary>
    /// Power Management e CPU core parking: ripristino, Ultimate Performance, tweak registro.
    /// </summary>
    public sealed class PowerOptimizationService
    {
        private readonly SystemRestoreService _systemRestore;

        /// <summary>Schema segreto Ultimate Performance (duplicabile).</summary>
        private const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        /// <summary>Piano Prestazioni elevate (fallback).</summary>
        private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        private const string CoreParkingSubKey =
            @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583";

        private static readonly Regex GuidRegex = new(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        public event Action<string, string>? Log;

        public PowerOptimizationService(SystemRestoreService systemRestore) =>
            _systemRestore = systemRestore;

        private void Emit(string msg, string level) => Log?.Invoke(msg, level);

        /// <summary>
        /// Ordine: punto di ripristino, <c>powercfg</c> (duplicate + setactive), core parking nel registro.
        /// </summary>
        public async Task OptimizeAsync(CancellationToken cancellationToken = default)
        {
            (bool Success, uint _, string? ErrorMessage) rp =
                await _systemRestore.CreateRestorePointAsync(
                    "SysSuite Power Optimization",
                    cancellationToken).ConfigureAwait(false);

            if (rp.Success)
                Emit("Punto di ripristino creato (SysSuite Power Optimization).", "ok");
            else
                Emit(
                    "Punto di ripristino non creato: "
                    + (rp.ErrorMessage ?? "WMI")
                    + ". Procedo con powercfg e registro.",
                    "warn");

            await ApplyUltimatePerformancePlanAsync(cancellationToken)
                .ConfigureAwait(false);

            await Task.Run(() => ApplyCoreParkingRegistry(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            Emit("Core Parking: ValueMax e ValueMin impostati a 0.", "ok");
        }

        private async Task ApplyUltimatePerformancePlanAsync(CancellationToken cancellationToken)
        {
            string? newGuid = null;
            (int dupExit, string dupOut) = await ProcessRunner.RunCaptureAsync(
                    "powercfg.exe",
                    "-duplicatescheme " + UltimateTemplateGuid,
                    cancellationToken)
                .ConfigureAwait(false);

            newGuid = ExtractLastGuid(dupOut);
            if (dupExit != 0 && string.IsNullOrEmpty(newGuid))
                Emit("powercfg -duplicatescheme: codice " + dupExit + ".", "warn");

            if (!string.IsNullOrEmpty(newGuid))
            {
                int setExit = await TrySetActiveAsync(newGuid, cancellationToken).ConfigureAwait(false);
                if (setExit == 0)
                {
                    Emit("Piano attivo (GUID duplicato): " + newGuid, "ok");
                    return;
                }
            }

            if (await TrySetActiveAsync(UltimateTemplateGuid, cancellationToken).ConfigureAwait(false) == 0)
            {
                Emit("Piano Ultimate Performance attivo (GUID template).", "ok");
                return;
            }

            if (await TrySetActiveAsync(HighPerformanceGuid, cancellationToken).ConfigureAwait(false) == 0)
            {
                Emit("Piano Prestazioni elevate attivo (fallback).", "ok");
                return;
            }

            throw new InvalidOperationException(
                "Impossibile attivare un piano prestazioni elevato (powercfg -setactive fallito).");
        }

        private static string? ExtractLastGuid(string text)
        {
            MatchCollection matches = GuidRegex.Matches(text);
            return matches.Count == 0 ? null : matches[^1].Value;
        }

        private static async Task<int> TrySetActiveAsync(string schemeGuid, CancellationToken cancellationToken)
        {
            (int exit, _) = await ProcessRunner.RunCaptureAsync(
                    "powercfg.exe",
                    "-setactive " + schemeGuid,
                    cancellationToken)
                .ConfigureAwait(false);
            return exit;
        }

        private static void ApplyCoreParkingRegistry(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(CoreParkingSubKey, writable: true);
            if (key == null)
                throw new InvalidOperationException(
                    "Chiave Core Parking assente (HKLM\\SYSTEM\\...\\0cc5b647-c1df-4637-891a-dec35c318583).");

            key.SetValue("ValueMax", 0, RegistryValueKind.DWord);
            key.SetValue("ValueMin", 0, RegistryValueKind.DWord);
        }
    }
}
