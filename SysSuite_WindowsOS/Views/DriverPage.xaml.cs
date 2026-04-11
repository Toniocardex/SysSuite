using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class DriverPage : Page
    {
        public DriverPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<DriverViewModel>();
        }
    }
}
