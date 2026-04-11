using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class DiscoPage : Page
    {
        public DiscoPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<DiscoViewModel>();
        }
    }
}
