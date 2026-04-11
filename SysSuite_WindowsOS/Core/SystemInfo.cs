using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SysSuite.Core
{
    /// <summary>Riepilogo hardware e OS via WMI e API locali.</summary>
    public class SystemInfo
    {
        private const string Nd = "N/D";

        // ── Sistema operativo ──────────────────────────────────────
        public string OSName { get; private set; } = "";
        public string OSVersion { get; private set; } = "";
        public string OSBuild { get; private set; } = "";

        // ── CPU ───────────────────────────────────────────────────
        public string CPUName { get; private set; } = "";
        public int CPUCores { get; private set; }
        public int CPUThreads { get; private set; }
        public int CPUMaxGHz { get; private set; }
        public string CPUArch { get; private set; } = "";

        // ── RAM ───────────────────────────────────────────────────
        public double RAMTotalGB { get; private set; }
        public double RAMFreeGB { get; private set; }
        public int RAMCommercialGB { get; private set; }

        /// <summary>Riga riepilogo moduli fisici: capacità, tipo DDR, MHz.</summary>
        public string RamHardwareDetails { get; private set; } = Nd;

        // ── Disco C: ──────────────────────────────────────────────
        public double DiskTotalGB { get; private set; }
        public double DiskFreeGB { get; private set; }

        /// <summary>Modello unità fisica del volume di sistema (es. Samsung SSD 980 PRO).</summary>
        public string SystemDiskModel { get; private set; } = Nd;

        // ── GPU / Display ─────────────────────────────────────────
        public string GPUName { get; private set; } = "";
        public string GPUVRAMStr { get; private set; } = "";

        /// <summary>Risoluzione corrente e frequenza (es. 2560 × 1440 · 165 Hz).</summary>
        public string DisplayDetails { get; private set; } = Nd;

        // ── Scheda madre ───────────────────────────────────────────
        public string MotherboardModel { get; private set; } = Nd;

        // ── Rete ──────────────────────────────────────────────────
        public string LocalIP { get; private set; } = "";

        // ── Uptime ────────────────────────────────────────────────
        public TimeSpan Uptime { get; private set; }

        public double RAMUsedPct => RAMTotalGB > 0
            ? Math.Round((RAMTotalGB - RAMFreeGB) / RAMTotalGB * 100, 1) : 0;
        public double DiskUsedPct => DiskTotalGB > 0
            ? Math.Round((DiskTotalGB - DiskFreeGB) / DiskTotalGB * 100, 1) : 0;
        public string CPUFreqStr => CPUMaxGHz > 0
            ? (CPUMaxGHz / 1000.0).ToString("0.0") + " GHz" : "";

        public static int ToCommercialRAM(double actualGB)
        {
            int[] slots = { 4, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256 };
            foreach (int s in slots)
                if (actualGB <= s + 0.5) return s;
            return (int)Math.Ceiling(actualGB / 4.0) * 4;
        }

        /// <summary>Raccolta completa (WMI + DriveInfo). Chiamare da Task.Run per non bloccare la UI.</summary>
        public static SystemInfo Collect()
        {
            var info = new SystemInfo();
            info.GatherAll();
            return info;
        }

        /// <summary>Popola tutte le proprietà; errori parziali → <c>N/D</c> sul singolo campo.</summary>
        public void GatherAll()
        {
            TryCollectOperatingSystem(this);
            TryCollectCpu(this);
            TryCollectLogicalDiskSizes(this);
            TryCollectGpuAndDisplay(this);
            TryCollectLocalIp(this);
            TryCollectPhysicalRamDetails(this);
            TryCollectMotherboard(this);
            TryCollectSystemDiskModel(this);
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
                    RAMFreeGB = Math.Round(freeKB / 1024 / 1024, 1);
                    var boot = ManagementDateTimeConverter.ToDateTime(o["LastBootUpTime"]?.ToString() ?? "");
                    Uptime = DateTime.Now - boot;
                }

                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(sysDrive);
                DiskFreeGB = Math.Round(drive.AvailableFreeSpace / 1073741824.0, 1);
            }
            catch { }
        }

        private static void TryCollectOperatingSystem(SystemInfo info)
        {
            try
            {
                using var osQ = new ManagementObjectSearcher(
                    "SELECT Caption,Version,BuildNumber,TotalVisibleMemorySize,FreePhysicalMemory,LastBootUpTime FROM Win32_OperatingSystem");
                foreach (ManagementObject o in osQ.Get())
                {
                    info.OSName = o["Caption"]?.ToString() ?? "";
                    info.OSVersion = o["Version"]?.ToString() ?? "";
                    info.OSBuild = o["BuildNumber"]?.ToString() ?? "";
                    double totalKB = Convert.ToDouble(o["TotalVisibleMemorySize"]);
                    double freeKB = Convert.ToDouble(o["FreePhysicalMemory"]);
                    info.RAMTotalGB = Math.Round(totalKB / 1024 / 1024, 1);
                    info.RAMFreeGB = Math.Round(freeKB / 1024 / 1024, 1);
                    info.RAMCommercialGB = ToCommercialRAM(info.RAMTotalGB);
                    var boot = ManagementDateTimeConverter.ToDateTime(o["LastBootUpTime"]?.ToString() ?? "");
                    info.Uptime = DateTime.Now - boot;
                }
            }
            catch { }
        }

        private static void TryCollectCpu(SystemInfo info)
        {
            try
            {
                using var cpuQ = new ManagementObjectSearcher(
                    "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,AddressWidth FROM Win32_Processor");
                foreach (ManagementObject o in cpuQ.Get())
                {
                    info.CPUName = o["Name"]?.ToString()?.Trim() ?? "";
                    info.CPUCores = Convert.ToInt32(o["NumberOfCores"]);
                    info.CPUThreads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                    info.CPUMaxGHz = Convert.ToInt32(o["MaxClockSpeed"]);
                    int bits = Convert.ToInt32(o["AddressWidth"]);
                    info.CPUArch = bits == 64 ? "x64" : bits == 32 ? "x86" : "ARM";
                    break;
                }
            }
            catch { }
        }

        private static void TryCollectLogicalDiskSizes(SystemInfo info)
        {
            try
            {
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(sysDrive);
                info.DiskTotalGB = Math.Round(drive.TotalSize / 1073741824.0, 1);
                info.DiskFreeGB = Math.Round(drive.AvailableFreeSpace / 1073741824.0, 1);
            }
            catch { }
        }

        private static void TryCollectGpuAndDisplay(SystemInfo info)
        {
            info.DisplayDetails = Nd;
            try
            {
                using var gpuQ = new ManagementObjectSearcher(
                    "SELECT Caption,AdapterRAM,VideoProcessor,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController");
                long bestVram = -1;
                int bestPixels = -1;
                string? bestDisplay = null;

                foreach (ManagementObject o in gpuQ.Get())
                {
                    try
                    {
                        string name = o["Caption"]?.ToString() ?? "";
                        long vram = 0;
                        try { vram = Convert.ToInt64(o["AdapterRAM"]); } catch { }

                        if (vram > bestVram || bestVram < 0)
                        {
                            bestVram = vram;
                            info.GPUName = name;
                            info.GPUVRAMStr = vram > 0
                                ? Math.Round(vram / 1073741824.0, 0) + " GB"
                                : "Condivisa";
                        }

                        int w = SafeToInt(o["CurrentHorizontalResolution"]);
                        int h = SafeToInt(o["CurrentVerticalResolution"]);
                        int hz = SafeToInt(o["CurrentRefreshRate"]);
                        int px = w * h;
                        if (w > 0 && h > 0 && hz > 0 && px >= bestPixels)
                        {
                            bestPixels = px;
                            bestDisplay = w + " × " + h + " · " + hz + " Hz";
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(bestDisplay))
                    info.DisplayDetails = bestDisplay!;
            }
            catch { }
        }

        private static int SafeToInt(object? v)
        {
            try
            {
                if (v == null) return 0;
                return Convert.ToInt32(v);
            }
            catch { return 0; }
        }

        /// <summary>MHz da Speed o, se assente, ConfiguredClockSpeed (Win32_PhysicalMemory).</summary>
        private static int ReadSpeedMhz(ManagementObject o)
        {
            foreach (string key in new[] { "Speed", "ConfiguredClockSpeed" })
            {
                try
                {
                    if (o[key] == null) continue;
                    int v = Convert.ToInt32(o[key]);
                    if (v > 0)
                        return v;
                }
                catch { }
            }

            return 0;
        }

        private static void TryCollectLocalIp(SystemInfo info)
        {
            try
            {
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
        }

        /// <summary>
        /// <see cref="Win32_PhysicalMemory"/> — campo <c>SMBIOSMemoryType</c> (tabella SMBIOS Memory Device, es. 24=DDR3, 26=DDR4, 34=DDR5), con fallback su <c>MemoryType</c> legacy.
        /// </summary>
        private static void TryCollectPhysicalRamDetails(SystemInfo info)
        {
            info.RamHardwareDetails = Nd;
            try
            {
                using var q = new ManagementObjectSearcher(
                    "SELECT Capacity,Speed,ConfiguredClockSpeed,SMBIOSMemoryType,MemoryType FROM Win32_PhysicalMemory");
                ulong totalBytes = 0;
                var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var speeds = new HashSet<int>();
                int modules = 0;

                foreach (ManagementObject o in q.Get())
                {
                    try
                    {
                        modules++;
                        if (o["Capacity"] != null)
                            totalBytes += Convert.ToUInt64(o["Capacity"]);

                        int speed = ReadSpeedMhz(o);
                        if (speed > 0)
                            speeds.Add(speed);

                        string typeLabel = Nd;
                        try
                        {
                            if (o["SMBIOSMemoryType"] != null)
                            {
                                ushort smb = Convert.ToUInt16(o["SMBIOSMemoryType"]);
                                if (smb != 0)
                                    typeLabel = MapSmbiosMemoryType(smb);
                            }
                        }
                        catch { }

                        if (typeLabel == Nd && o["MemoryType"] != null)
                        {
                            try
                            {
                                ushort legacy = Convert.ToUInt16(o["MemoryType"]);
                                typeLabel = MapLegacyMemoryType(legacy);
                            }
                            catch { }
                        }

                        if (typeLabel != Nd)
                            types.Add(typeLabel);
                    }
                    catch { }
                }

                if (modules == 0)
                    return;

                double totalGb = Math.Round(totalBytes / 1073741824.0, 1);
                string typeStr = types.Count > 0 ? string.Join(" / ", types.OrderBy(t => t)) : Nd;
                string speedStr = Nd;
                if (speeds.Count > 0)
                    speedStr = string.Join(" / ", speeds.OrderBy(s => s)) + " MHz";

                string dimm = modules == 1 ? "1 DIMM" : modules + " DIMM";
                info.RamHardwareDetails = totalGb.ToString("0.#") + " GB · " + typeStr + " · " + speedStr + " (" + dimm + ")";
            }
            catch { }
        }

        /// <summary>Tabella SMBIOS Memory Device — Type (campo SMBIOSMemoryType di Win32_PhysicalMemory).</summary>
        private static string MapSmbiosMemoryType(ushort code) => code switch
        {
            1 => "Other",
            2 => "Unknown",
            3 => "DRAM",
            15 => "SDRAM",
            16 => "SGRAM",
            17 => "RDRAM",
            18 => "DDR",
            19 => "DDR2",
            20 => "DDR2 FB-DIMM",
            24 => "DDR3",
            25 => "FBD2",
            26 => "DDR4",
            27 => "LPDDR4",
            28 => "LPDDR4X",
            30 => "LPDDR5",
            34 => "DDR5",
            35 => "LPDDR5",
            36 => "LPDDR5X",
            _ => "Tipo " + code
        };

        /// <summary>Campo legacy MemoryType (WMIMemoryType).</summary>
        private static string MapLegacyMemoryType(ushort code) => code switch
        {
            0 => "Unknown",
            1 => "Other",
            2 => "DRAM",
            3 => "Synchronous DRAM",
            4 => "Cache DRAM",
            5 => "EDO",
            6 => "EDRAM",
            7 => "VRAM",
            8 => "SRAM",
            9 => "RAM",
            10 => "ROM",
            11 => "FLASH",
            12 => "EEPROM",
            13 => "FEPROM",
            14 => "EPROM",
            15 => "CDRAM",
            16 => "3DRAM",
            17 => "SDRAM",
            18 => "SGRAM",
            19 => "RDRAM",
            20 => "DDR",
            21 => "DDR2",
            22 => "DDR2 FB-DIMM",
            24 => "DDR3",
            25 => "FBD2",
            26 => "DDR4",
            27 => "DDR5",
            _ => "Tipo " + code
        };

        private static void TryCollectMotherboard(SystemInfo info)
        {
            info.MotherboardModel = Nd;
            try
            {
                using var q = new ManagementObjectSearcher(
                    "SELECT Manufacturer,Product,Version FROM Win32_BaseBoard");
                foreach (ManagementObject o in q.Get())
                {
                    string m = o["Manufacturer"]?.ToString()?.Trim() ?? "";
                    string p = o["Product"]?.ToString()?.Trim() ?? "";
                    string v = o["Version"]?.ToString()?.Trim() ?? "";
                    string line = string.Join(" ", new[] { m, p }.Where(s => s.Length > 0));
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (!string.IsNullOrEmpty(v) && !p.Equals(v, StringComparison.OrdinalIgnoreCase)
                        && !line.Contains(v, StringComparison.OrdinalIgnoreCase))
                        line += " (" + v + ")";
                    info.MotherboardModel = line;
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// Modello <see cref="Win32_DiskDrive"/> per il volume di sistema (es. C:): prima
        /// <see cref="Win32_LogicalDiskToPartition"/> → indice disco; fallback partizione di boot
        /// (<see cref="Win32_DiskPartition"/> con <c>BootPartition=True</c>).
        /// </summary>
        private static void TryCollectSystemDiskModel(SystemInfo info)
        {
            info.SystemDiskModel = Nd;
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                string driveId = root.TrimEnd('\\', '/'); // "C:"

                if (TryResolveSystemDiskIndex(driveId, out int diskIndex))
                {
                    string? m = QueryDiskDriveModelByIndex(diskIndex);
                    if (!string.IsNullOrEmpty(m))
                    {
                        info.SystemDiskModel = m;
                        return;
                    }
                }
            }
            catch { }

            try
            {
                using var q = new ManagementObjectSearcher(
                    "SELECT DiskIndex FROM Win32_DiskPartition WHERE BootPartition=True");
                foreach (ManagementObject part in q.Get())
                {
                    try
                    {
                        int idx = Convert.ToInt32(part["DiskIndex"]);
                        string? m = QueryDiskDriveModelByIndex(idx);
                        if (!string.IsNullOrEmpty(m))
                        {
                            info.SystemDiskModel = m;
                            return;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Collega il volume logico (DeviceID es. C:) al numero disco fisico via WMI.</summary>
        private static bool TryResolveSystemDiskIndex(string logicalDeviceId, out int diskIndex)
        {
            diskIndex = -1;
            try
            {
                string needle = "DeviceID=\"" + logicalDeviceId + "\"";
                using var q = new ManagementObjectSearcher(
                    "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
                foreach (ManagementObject o in q.Get())
                {
                    try
                    {
                        string? dep = o["Dependent"]?.ToString();
                        if (string.IsNullOrEmpty(dep)
                            || dep.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        string? ant = o["Antecedent"]?.ToString();
                        if (string.IsNullOrEmpty(ant))
                            continue;

                        var m = Regex.Match(ant, @"Disk\s*#\s*(\d+)", RegexOptions.IgnoreCase);
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int idx) && idx >= 0)
                        {
                            diskIndex = idx;
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        private static string? QueryDiskDriveModelByIndex(int diskIndex)
        {
            try
            {
                using var q2 = new ManagementObjectSearcher(
                    "SELECT Model FROM Win32_DiskDrive WHERE Index=" + diskIndex);
                foreach (ManagementObject disk in q2.Get())
                {
                    string? m = disk["Model"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(m))
                        return m;
                }
            }
            catch { }

            return null;
        }
    }
}
