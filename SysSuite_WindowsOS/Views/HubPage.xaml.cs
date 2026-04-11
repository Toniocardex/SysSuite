using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class HubPage : Page
    {
        public HubViewModel ViewModel { get; }

        public HubPage()
        {
            InitializeComponent();
            ViewModel = App.Services.GetRequiredService<HubViewModel>();
            DataContext = ViewModel;
            this.Loaded += (s, e) => ViewModel.LoadDashboardDataCommand.Execute(null);
        }
    }
}
