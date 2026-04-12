namespace SysSuite.Models;

/// <summary>
/// Solo testi statici per lo snapshot hardware (DTO per <c>x:Bind</c> OneTime sulla Dashboard).
/// </summary>
public sealed class HardwareSnapshotDisplay
{
    public string Processor { get; }
    public string GpuModel { get; }
    public string Ram { get; }
    public string Motherboard { get; }
    public string DisplayText { get; }

    public HardwareSnapshotDisplay(
        string processor,
        string gpuModel,
        string ram,
        string motherboard,
        string display)
    {
        Processor = processor;
        GpuModel = gpuModel;
        Ram = ram;
        Motherboard = motherboard;
        DisplayText = display;
    }

    public static HardwareSnapshotDisplay Empty { get; } = new("—", "—", "N/D", "N/D", "N/D");
}
