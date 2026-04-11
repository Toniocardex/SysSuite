using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class NetworkPage : Page
    {
        public NetworkPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<NetworkViewModel>();
        }
    }
}
