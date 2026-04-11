using Microsoft.UI.Xaml;

namespace SysSuite.Models
{
    /// <summary>Riga lista Processi e App (tre colonne + voce avvio opzionale).</summary>
    public class AppManagerRow
    {
        public string Col0 { get; set; } = "";
        public string Col1 { get; set; } = "";
        public string Col2 { get; set; } = "";
        public StartupEntry? Startup { get; set; }
        public string UninstallString { get; set; } = "";
        public string DisplayIcon { get; set; } = "";
        public bool ShowUninstall { get; set; }
        public Visibility UninstallButtonVisibility { get; set; } = Visibility.Collapsed;
    }
}
