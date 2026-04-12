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
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<HubViewModel>();
        DataContext = ViewModel;
        Loaded += (_, _) => ViewModel.LoadDashboardDataCommand.Execute(null);
        Unloaded += (_, _) => ViewModel.Dispose();
    }
}
