namespace SysSuite.Models
{
    /// <summary>Dati di un punto di ripristino (WMI <c>root\default:SystemRestore</c>).</summary>
    public sealed class SystemRestorePointInfo
    {
        public uint SequenceNumber { get; init; }
        public string Description { get; init; } = "";
        public uint RestorePointType { get; init; }
        public uint EventType { get; init; }
        /// <summary>Stringa WMI non elaborata (es. <c>20240412103000.000000+***</c>).</summary>
        public string CreationTimeRaw { get; init; } = "";
        /// <summary>Data/ora formattata per la UI; vuota se il parsing fallisce.</summary>
        public string CreationTimeDisplay { get; init; } = "";
    }
}
