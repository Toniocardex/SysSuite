namespace SysSuite.Models
{
    public class DiskEntry
    {
        public string   Path        { get; set; } = "";
        public string   Name        { get; set; } = "";
        public long     SizeBytes   { get; set; }
        public int      FileCount   { get; set; }
        public int      FolderCount { get; set; }
        public DateTime LastWrite   { get; set; }
        public string SizeFormatted => FormatSize(SizeBytes);
        public static string FormatSize(long b) =>
            b >= 1_073_741_824 ? $"{b/1_073_741_824.0:F1} GB" :
            b >= 1_048_576     ? $"{b/1_048_576.0:F1} MB"     :
            b >= 1_024         ? $"{b/1_024.0:F0} KB"          : $"{b} B";
    }

    public class DuplicateGroup
    {
        public string          FileName    { get; set; } = "";
        public long            SizeBytes   { get; set; }
        public string          Hash        { get; set; } = "";
        public List<DiskEntry> Files       { get; set; } = new();
        public long            WastedBytes => SizeBytes * (Files.Count - 1);
    }

    public class SmartData
    {
        public string DiskModel     { get; set; } = "";
        public string SerialNumber  { get; set; } = "";
        public string Interface     { get; set; } = "";
        public long   SizeBytes     { get; set; }
        public bool   FailPredicted { get; set; }
        public Dictionary<string,int> Attributes { get; set; } = new();
    }
}