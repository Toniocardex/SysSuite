using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class AppManagerPage : Page
    {
        public AppManagerViewModel ViewModel { get; }

        public AppManagerPage()
        {
            InitializeComponent();
            ViewModel = App.Services.GetRequiredService<AppManagerViewModel>();
            DataContext = ViewModel;
            this.Loaded += (s, e) => ViewModel.LoadDataCommand.Execute(null);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
                ViewModel.SearchText = tb.Text;
        }

        private async void BtnDisable_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Items.Count == 0 || LvItems.SelectedItem is not AppManagerRow row || row.Startup == null)
            {
                ViewModel.StatusText = "Seleziona una voce di avvio.";
                return;
            }

            if (row.Startup.Source.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                && !AdminHelper.IsAdmin())
            {
                ViewModel.StatusText = "Voci HKLM richiedono Admin. Usa 'Riavvia come Admin' in basso a destra.";
                return;
            }

            var confirm = new ContentDialog
            {
                Title = "Disabilita avvio automatico",
                Content = "Disabilitare " + row.Col0 + " dall'avvio automatico?\n"
                    + "Origine: " + row.Col1,
                PrimaryButtonText = "Disabilita",
                CloseButtonText = "Annulla",
                XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            bool ok = await ViewModel.DisableStartupForRowAsync(row).ConfigureAwait(true);
            ViewModel.StatusText = ok ? "Disabilitato: " + row.Col0 : "Errore disabilitazione.";
            ViewModel.LoadStartupCommand.Execute(null);
        }
    }
}
