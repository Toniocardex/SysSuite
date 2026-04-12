using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.Core;
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

        private void LogCopyMenu_Click(object sender, RoutedEventArgs e) =>
            LogClipboardHelper.CopyToClipboard(ViewModel.LogText);
    }
}
