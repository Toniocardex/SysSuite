using Microsoft.UI.Xaml.Media;

namespace SysSuite.Models
{
    /// <summary>Riga lista driver per binding (include brush stato).</summary>
    public sealed class DriverListRowUi
    {
        public string Name { get; init; } = "";
        public string Manufacturer { get; init; } = "";
        public string Version { get; init; } = "";
        public string Date { get; init; } = "";
        public string DeviceClass { get; init; } = "";
        public string Status { get; init; } = "";
        public SolidColorBrush StatusColor { get; init; } = null!;
    }
}
