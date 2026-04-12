using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views;

public sealed partial class DashboardPage : Page
{
    public HubViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<HubViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += (_, _) => ViewModel.LoadDashboardDataCommand.Execute(null);
        Unloaded += (_, _) => ViewModel.Dispose();
    }
}
