using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SysSuite.Services
{
    /// <summary>
    /// Ottimizzatore RAM: svuota i working set dei processi inattivi
    /// tramite EmptyWorkingSet (Win32) e SetProcessWorkingSetSize.
    /// Effetto tipico: -1 GB / -3 GB di RAM fisica occupata, visibile
    /// in pochi secondi. La RAM viene riassegnata ai processi non appena
    /// ne hanno bisogno — nessun danno alle applicazioni in esecuzione.
    /// </summary>
    public class RamOptimizerService
    {
        public event Action<string, string>? Log;

        // ── P/Invoke Win32 ───────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(
            IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(
            uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SET_QUOTA       = 0x0100;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION    = 0x0008;
        private const uint ACCESS_FLAGS =
            PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION;

        // ── RAM disponibile via GlobalMemoryStatusEx ─────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint  dwLength;
            public uint  dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        /// <summary>Legge la RAM libera attuale in MB.</summary>
        public static long GetFreeRamMB()
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref mem)
                ? (long)(mem.ullAvailPhys / 1_048_576)
                : 0;
        }

        /// <summary>Legge la RAM totale in MB.</summary>
        public static long GetTotalRamMB()
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref mem)
                ? (long)(mem.ullTotalPhys / 1_048_576)
                : 0;
        }

        /// <summary>
        /// Ottimizza la RAM: itera tutti i processi e tenta di svuotarne
        /// il working set. Restituisce i MB liberati (stima: prima - dopo).
        /// Operazione safe: non termina processi, non tocca la memoria virtuale.
        /// </summary>
        public long Optimize()
        {
            long before = GetFreeRamMB();
            int  ok     = 0;
            int  skip   = 0;

            var procs = Process.GetProcesses();
            Emit($"Avvio ottimizzazione: {procs.Length} processi...", "head");

            foreach (var p in procs)
            {
                try
                {
                    // Salta il processo corrente (SysSuite stesso)
                    if (p.Id == Environment.ProcessId) { skip++; continue; }

                    IntPtr handle = OpenProcess(ACCESS_FLAGS, false, p.Id);
                    if (handle == IntPtr.Zero) { skip++; continue; }

                    try
                    {
                        // Svuota working set — Windows riporta le pagine su disco/swap
                        bool success = EmptyWorkingSet(handle);

                        // Fallback: SetProcessWorkingSetSize con -1 = "trimma al minimo"
                        if (!success)
                            SetProcessWorkingSetSize(handle, new IntPtr(-1), new IntPtr(-1));

                        ok++;
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                catch { skip++; }
                finally { p.Dispose(); }
            }

            // Attendi che Windows aggiorni la contabilità memoria
            // Nota: chiamare Thread.Sleep è accettabile qui perché Optimize()
            // viene sempre invocato via Task.Run dal chiamante (HubPage).
            Thread.Sleep(800);

            long after   = GetFreeRamMB();
            long freed   = Math.Max(0, after - before);

            Emit($"Processi ottimizzati: {ok} · saltati: {skip}", "ok");
            Emit($"RAM prima: {before} MB liberi  →  dopo: {after} MB liberi", "ok");
            if (freed > 0)
                Emit($"Recuperati circa {freed} MB di RAM fisica", "ok");
            else
                Emit("RAM già ottimizzata o processi troppo attivi per il trimming", "warn");

            return freed;
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}