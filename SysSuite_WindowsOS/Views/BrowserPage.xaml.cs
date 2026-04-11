using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class BrowserPage : Page
    {
        public BrowserPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<BrowserViewModel>();
        }
    }
}
