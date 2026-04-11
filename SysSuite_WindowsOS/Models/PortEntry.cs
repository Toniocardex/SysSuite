namespace SysSuite.Models
{
    public class PortEntry
    {
        public int    Port        { get; set; }
        public string Protocol   { get; set; } = "";
        public string State      { get; set; } = "";
        public string LocalAddr   { get; set; } = "";
        public string RemoteAddr  { get; set; } = "";
        public int    PID         { get; set; }
        public string ProcessName { get; set; } = "";
        public bool   IsSuspicious { get; set; }
    }
}
