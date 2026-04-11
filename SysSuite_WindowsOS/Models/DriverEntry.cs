namespace SysSuite.Models
{
    public class DriverEntry
    {
        public string Name         { get; set; } = "";
        public string Description  { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Version      { get; set; } = "";
        public string Date         { get; set; } = "";
        public string DeviceClass  { get; set; } = "";
        public bool   HasProblem   { get; set; }
        public string Status       { get; set; } = "";
    }

    public class BatteryInfo
    {
        public string Name            { get; set; } = "";
        public int    DesignCapacity  { get; set; }
        public int    FullCapacity    { get; set; }
        public int    CurrentCharge   { get; set; }
        public int    HealthPercent   => DesignCapacity > 0
            ? (int)Math.Round(FullCapacity * 100.0 / DesignCapacity) : 0;
        public int    ChargePercent   => FullCapacity > 0
            ? (int)Math.Round(CurrentCharge * 100.0 / FullCapacity) : 0;
        public string Status          { get; set; } = "";
        public int    CycleCount      { get; set; }
    }
}