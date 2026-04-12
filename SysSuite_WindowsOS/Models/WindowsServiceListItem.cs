namespace SysSuite.Models
{
    /// <summary>Riga leggera per elenco servizi (enumerazione nativa SCM).</summary>
    public sealed class WindowsServiceListItem
    {
        public string Name { get; init; } = "";
        public string DisplayName { get; init; } = "";
        /// <summary>Stato SCM (es. <see cref="System.ServiceProcess.ServiceControllerStatus"/>).</summary>
        public uint CurrentState { get; init; }
        public string StateText { get; init; } = "";
        /// <summary>Valore registro Start (0–4) da <c>HKLM\...\Services\&lt;nome&gt;\Start</c>.</summary>
        public uint StartType { get; init; }
        public string StartTypeText { get; init; } = "";
        public uint ProcessId { get; init; }
    }
}
