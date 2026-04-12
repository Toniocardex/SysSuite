using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.Core;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class GamingPage : Page
    {
        public GamingPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<GamingViewModel>();
        }

        private void LogCopyMenu_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GamingViewModel vm)
                LogClipboardHelper.CopyToClipboard(vm.LogText);
        }
    }
}
