using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    /// <summary>Riga browser per ListView (Pulizia Browser).</summary>
    public partial class BrowserRowUi : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _cachePath = "";
        [ObservableProperty] private string _cacheSize = "";
        [ObservableProperty] private string _installedText = "";
        [ObservableProperty] private SolidColorBrush _installedColor = null!;
        public BrowserService.BrowserInfo Info { get; set; } = null!;
    }
}
