using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class BrowserPage : Page
    {
        public BrowserViewModel ViewModel { get; }

        public BrowserPage()
        {
            InitializeComponent();
            DataContext = ViewModel = App.Services.GetRequiredService<BrowserViewModel>();
            this.Loaded += (s, e) => ViewModel.LoadDataCommand.Execute(null);
        }
    }
}
