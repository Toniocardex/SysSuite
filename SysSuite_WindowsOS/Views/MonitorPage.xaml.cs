using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class MonitorPage : Page
    {
        private readonly MonitorViewModel _viewModel;

        public MonitorPage()
        {
            InitializeComponent();
            _viewModel = App.Services.GetRequiredService<MonitorViewModel>();
            DataContext = _viewModel;
        }

        private void MonitorPage_Loaded(object sender, RoutedEventArgs e) =>
            _viewModel.StartMonitoringCommand.Execute(null);

        private void MonitorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.StopMonitoring();
            _viewModel.Dispose();
        }
    }
}
