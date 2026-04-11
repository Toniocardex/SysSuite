using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class MonitorPage : Page
    {
        public MonitorViewModel ViewModel { get; }

        public MonitorPage()
        {
            InitializeComponent();
            ViewModel = App.Services.GetRequiredService<MonitorViewModel>();
            DataContext = ViewModel;
        }

        private void MonitorPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.StartMonitoringCommand.Execute(null);

        private void MonitorPage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.StopMonitoring();
    }
}
