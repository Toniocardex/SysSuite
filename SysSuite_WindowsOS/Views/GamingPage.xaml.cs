using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class GamingPage : Page
    {
        public GamingPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<GamingViewModel>();
        }
    }
}
