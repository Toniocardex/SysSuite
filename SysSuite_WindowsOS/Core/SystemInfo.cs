using System;
using System.Management;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SysSuite.Core
{
    public class SystemInfo
    {
        // ── Sistema operativo ──────────────────────────────────────
        public string OSName      { get; private set; } = "";
        public string OSVersion   { get; private set; } = "";
        public string OSBuild     { get; private set; } = "";   // es. 22631

        // ── CPU ───────────────────────────────────────────────────
        public string CPUName     { get; private set; } = "";
        public int    CPUCores    { get; private set; }
        public int    CPUThreads  { get; private set; }
        public int    CPUMaxGHz   { get; private set; }         // MHz → dividere per 1000
        public string CPUArch     { get; private set; } = "";  // x64, ARM64...

        // ── RAM ───────────────────────────────────────────────────
        public double RAMTotalGB        { get; private set; }
        public double RAMFreeGB         { get; private set; }
        /// <summary>RAM arrotondata al taglio commerciale standard (4/8/16/32/64/128 GB).</summary>
        public int    RAMCommercialGB   { get; private set; }

        // ── Disco C: ──────────────────────────────────────────────
        public double DiskTotalGB { get; private set; }
        public double DiskFreeGB  { get; private set; }

        // ── GPU ───────────────────────────────────────────────────
        public string GPUName     { get; private set; } = "";
        public string GPUVRAMStr  { get; private set; } = "";  // es. "4 GB" o "Condivisa"

        // ── Rete ──────────────────────────────────────────────────
        public string LocalIP     { get; private set; } = "";

        // ── Uptime ────────────────────────────────────────────────
        public TimeSpan Uptime    { get; private set; }

        // ── Calcolati ─────────────────────────────────────────────
        public double RAMUsedPct  => RAMTotalGB > 0
            ? Math.Round((RAMTotalGB - RAMFreeGB) / RAMTotalGB * 100, 1) : 0;
        public double DiskUsedPct => DiskTotalGB > 0
            ? Math.Round((DiskTotalGB - DiskFreeGB) / DiskTotalGB * 100, 1) : 0;
        public string CPUFreqStr  => CPUMaxGHz > 0
            ? (CPUMaxGHz / 1000.0).ToString("0.0") + " GHz" : "";

        // ── Arrotondamento commerciale RAM ─────────────────────────
        /// <summary>
        /// Restituisce la dimensione RAM arrotondata al taglio commerciale
        /// più vicino per eccesso: 4 / 8 / 12 / 16 / 24 / 32 / 48 / 64 / 128 GB.
        /// Es: 15,6 GB → 16 GB  |  7,9 GB → 8 GB  |  31,5 GB → 32 GB
        /// </summary>
        public static int ToCommercialRAM(double actualGB)
        {
            int[] slots = { 4, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256 };
            foreach (int s in slots)
                if (actualGB <= s + 0.5) return s;
            return (int)Math.Ceiling(actualGB / 4.0) * 4;
        }

        // ── Raccolta dati ──────────────────────────────────────────
        public static SystemInfo Collect()
        {
            var info = new SystemInfo();
            try
            {
                // OS
                using var osQ = new ManagementObjectSearcher(
                    "SELECT Caption,Version,BuildNumber,TotalVisibleMemorySize,FreePhysicalMemory,LastBootUpTime FROM Win32_OperatingSystem");
                foreach (ManagementObject o in osQ.Get())
                {
                    info.OSName    = o["Caption"]?.ToString() ?? "";
                    info.OSVersion = o["Version"]?.ToString() ?? "";
                    info.OSBuild   = o["BuildNumber"]?.ToString() ?? "";
                    double totalKB = Convert.ToDouble(o["TotalVisibleMemorySize"]);
                    double freeKB  = Convert.ToDouble(o["FreePhysicalMemory"]);
                    info.RAMTotalGB = Math.Round(totalKB / 1024 / 1024, 1);
                    info.RAMFreeGB  = Math.Round(freeKB  / 1024 / 1024, 1);
                    info.RAMCommercialGB = ToCommercialRAM(info.RAMTotalGB);
                    var boot = ManagementDateTimeConverter.ToDateTime(o["LastBootUpTime"]?.ToString() ?? "");
                    info.Uptime = DateTime.Now - boot;
                }

                // CPU
                using var cpuQ = new ManagementObjectSearcher(
                    "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,AddressWidth FROM Win32_Processor");
                foreach (ManagementObject o in cpuQ.Get())
                {
                    info.CPUName    = o["Name"]?.ToString()?.Trim() ?? "";
                    info.CPUCores   = Convert.ToInt32(o["NumberOfCores"]);
                    info.CPUThreads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                    info.CPUMaxGHz  = Convert.ToInt32(o["MaxClockSpeed"]);
                    int bits        = Convert.ToInt32(o["AddressWidth"]);
                    info.CPUArch    = bits == 64 ? "x64" : bits == 32 ? "x86" : "ARM";
                    break;
                }

                // Disco di sistema (tipicamente C:, ma rispetta %SystemDrive%)
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive        = new DriveInfo(sysDrive);
                info.DiskTotalGB = Math.Round(drive.TotalSize / 1073741824.0, 1);
                info.DiskFreeGB  = Math.Round(drive.AvailableFreeSpace / 1073741824.0, 1);

                // GPU (prima scheda discreta, poi integrata)
                using var gpuQ = new ManagementObjectSearcher(
                    "SELECT Caption,AdapterRAM,VideoProcessor FROM Win32_VideoController");
                long bestVRAM = -1;
                foreach (ManagementObject o in gpuQ.Get())
                {
                    string name  = o["Caption"]?.ToString() ?? "";
                    long   vram  = 0;
                    try { vram = Convert.ToInt64(o["AdapterRAM"]); } catch { }
                    if (vram > bestVRAM || bestVRAM < 0)
                    {
                        bestVRAM       = vram;
                        info.GPUName   = name;
                        info.GPUVRAMStr = vram > 0
                            ? Math.Round(vram / 1073741824.0, 0) + " GB"
                            : "Condivisa";
                    }
                }

                // IP locale (prima interfaccia attiva non loopback)
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            info.LocalIP = addr.Address.ToString();
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(info.LocalIP)) break;
                }
            }
            catch { }
            return info;
        }

        public void Refresh()
        {
            try
            {
                using var osQ = new ManagementObjectSearcher(
                    "SELECT FreePhysicalMemory, LastBootUpTime FROM Win32_OperatingSystem");
                foreach (ManagementObject o in osQ.Get())
                {
                    double freeKB = Convert.ToDouble(o["FreePhysicalMemory"]);
                    RAMFreeGB     = Math.Round(freeKB / 1024 / 1024, 1);
                    var boot      = ManagementDateTimeConverter.ToDateTime(o["LastBootUpTime"]?.ToString() ?? "");
                    Uptime        = DateTime.Now - boot;
                }
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive  = new DriveInfo(sysDrive);
                DiskFreeGB = Math.Round(drive.AvailableFreeSpace / 1073741824.0, 1);
            }
            catch { }
        }
    }
}