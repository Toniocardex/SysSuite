namespace SysSuite.Models
{
    public class ProcessEntry
    {
        public int     PID         { get; set; }
        public string  Name        { get; set; } = "";
        public double  CpuPercent  { get; set; }
        public double  RamMB       { get; set; }
        public int     Threads     { get; set; }
        public int     Handles     { get; set; }
        public bool    Responding  { get; set; }
        public string  Path        { get; set; } = "";
        public string  Description { get; set; } = "";
        public DateTime StartTime  { get; set; }
    }

    public class StartupEntry
    {
        public string Name     { get; set; } = "";
        public string Command  { get; set; } = "";
        public string Source   { get; set; } = "";  // HKCU, HKLM, Folder
        public string RegPath  { get; set; } = "";
        public bool   Enabled  { get; set; } = true;
    }

    public class InstalledApp
    {
        public string Name        { get; set; } = "";
        public string Version     { get; set; } = "";
        public string Publisher   { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public long   SizeBytes   { get; set; }
        public string UninstallString { get; set; } = "";
        public int    DaysUnused  { get; set; }
    }
}