using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using SysSuite;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    public partial class AppManagerViewModel : ObservableObject
    {
        private readonly ProcessManager _pm;
        private readonly LeftoverScannerService _leftoverScanner;
        private string _mode = "";
        private List<AppManagerRow> _allItems = new();

        public AppManagerViewModel(ProcessManager pm, LeftoverScannerService leftoverScanner)
        {
            _pm = pm;
            _leftoverScanner = leftoverScanner;
        }

        public ObservableCollection<AppManagerRow> Items { get; } = new();

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _statusText = "Seleziona una vista per iniziare.";
        [ObservableProperty] private string _header0 = "NOME";
        [ObservableProperty] private string _header1 = "";
        [ObservableProperty] private string _header2 = "";
        [ObservableProperty] private string _searchText = "";

        public bool IsNotBusy => !IsBusy;

        partial void OnIsBusyChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotBusy));
            UninstallCommand.NotifyCanExecuteChanged();
        }

        partial void OnSearchTextChanged(string value)
        {
            if (_allItems.Count == 0) return;
            RefreshFilteredItems();
            if (string.IsNullOrWhiteSpace(SearchText))
                RestoreStatusForMode();
            else
                StatusText = Items.Count + " risultati";
        }

        private void RestoreStatusForMode()
        {
            StatusText = _mode switch
            {
                "startup" => _allItems.Count + " voci avvio",
                "apps" => _allItems.Count + " app installate",
                "unused" => _allItems.Count + " app non usate da 90+ giorni",
                _ => StatusText
            };
        }

        private void RefreshFilteredItems()
        {
            var q = SearchText.Trim().ToLowerInvariant();
            Items.Clear();
            IEnumerable<AppManagerRow> rows = _allItems;
            if (!string.IsNullOrEmpty(q))
            {
                rows = _allItems.Where(r =>
                    r.Col0.ToLowerInvariant().Contains(q)
                    || r.Col1.ToLowerInvariant().Contains(q)
                    || r.Col2.ToLowerInvariant().Contains(q));
            }

            foreach (var r in rows)
                Items.Add(r);
        }

        /// <summary>Aggiorna la lista visibile dopo cambio vista (evita filtro vecchio su dati nuovi).</summary>
        private void FinishViewSwitch()
        {
            if (!string.IsNullOrEmpty(SearchText))
                SearchText = string.Empty;
            else
                RefreshFilteredItems();
        }

        private static AppManagerRow MapInstalledRow(InstalledApp a)
        {
            bool show = !string.IsNullOrWhiteSpace(a.UninstallString);
            return new AppManagerRow
            {
                Col0 = a.Name,
                Col1 = a.Version,
                Col2 = a.Publisher,
                UninstallString = a.UninstallString,
                DisplayIcon = a.DisplayIcon,
                ShowUninstall = show,
                UninstallButtonVisibility = show ? Visibility.Visible : Visibility.Collapsed
            };
        }

        [RelayCommand]
        public async Task LoadDataAsync()
        {
            await LoadInstalledCoreAsync();
        }

        [RelayCommand]
        public async Task LoadStartupAsync()
        {
            IsBusy = true;
            try
            {
                var entries = await Task.Run(() => _pm.GetStartupEntries()).ConfigureAwait(true);
                _mode = "startup";
                Header0 = "NOME";
                Header1 = "ORIGINE";
                Header2 = "COMANDO";
                _allItems = entries.Select(e => new AppManagerRow
                {
                    Col0 = e.Name,
                    Col1 = e.Source,
                    Col2 = e.Command,
                    Startup = e,
                    ShowUninstall = false,
                    UninstallButtonVisibility = Visibility.Collapsed
                }).ToList();
                FinishViewSwitch();
                StatusText = _allItems.Count + " voci avvio";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task LoadInstalledAsync()
        {
            await LoadInstalledCoreAsync();
        }

        private async Task ReloadInstalledItemsCoreAsync()
        {
            var apps = await Task.Run(() => _pm.GetInstalledApps()).ConfigureAwait(true);
            _mode = "apps";
            Header0 = "NOME APP";
            Header1 = "VERSIONE";
            Header2 = "PRODUTTORE";
            _allItems = apps.Select(MapInstalledRow).ToList();
            FinishViewSwitch();
        }

        private async Task LoadInstalledCoreAsync()
        {
            IsBusy = true;
            try
            {
                await ReloadInstalledItemsCoreAsync().ConfigureAwait(true);
                StatusText = _allItems.Count + " app installate";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task LoadUnusedAsync()
        {
            IsBusy = true;
            StatusText = "Ricerca app inutilizzate...";
            Items.Clear();
            _allItems = new List<AppManagerRow>();
            try
            {
                var apps = await Task.Run(() => _pm.GetUnusedApps(90)).ConfigureAwait(true);
                _mode = "unused";
                Header0 = "NOME APP";
                Header1 = "ULTIMO USO";
                Header2 = "PRODUTTORE";
                _allItems = apps.Select(a =>
                {
                    bool show = !string.IsNullOrWhiteSpace(a.UninstallString);
                    return new AppManagerRow
                    {
                        Col0 = a.Name,
                        Col1 = a.DaysUnused + " gg fa",
                        Col2 = a.Publisher,
                        UninstallString = a.UninstallString,
                        DisplayIcon = a.DisplayIcon,
                        ShowUninstall = show,
                        UninstallButtonVisibility = show ? Visibility.Visible : Visibility.Collapsed
                    };
                }).ToList();
                FinishViewSwitch();
                StatusText = _allItems.Count + " app non usate da 90+ giorni";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore lettura app inutilizzate");
                StatusText = "Errore: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanUninstallApp(AppManagerRow? app) =>
            app != null
            && app.ShowUninstall
            && !string.IsNullOrWhiteSpace(app.UninstallString)
            && !IsBusy;

        [RelayCommand(CanExecute = nameof(CanUninstallApp))]
        private async Task Uninstall(AppManagerRow? app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.UninstallString))
                return;

            var xamlRoot = (MainWindow.Instance?.Content as FrameworkElement)?.XamlRoot;
            if (xamlRoot != null)
            {
                var dlg = new ContentDialog
                {
                    Title = "Disinstalla applicazione",
                    Content = "Disinstallare \"" + app.Col0 + "\"?\n\n"
                        + "Verrà avviato il programma di disinstallazione del produttore.",
                    PrimaryButtonText = "Disinstalla",
                    CloseButtonText = "Annulla",
                    XamlRoot = xamlRoot
                };
                if (await dlg.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }

            IsBusy = true;
            UninstallCommand.NotifyCanExecuteChanged();
            try
            {
                await _pm.UninstallAppAsync(app.UninstallString).ConfigureAwait(true);

                List<string> leftovers = new();
                try
                {
                    leftovers = await _leftoverScanner.ScanLeftoversAsync(app.Col0, app.Col2 ?? "")
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Scansione residui non completata dopo la disinstallazione di {App}", app.Col0);
                }

                if (leftovers.Count > 0 && xamlRoot != null)
                {
                    var residueDlg = new ContentDialog
                    {
                        Title = "Residui",
                        Content = "Disinstallazione completata. Trovati " + leftovers.Count
                            + " elementi residui (file/chiavi). Vuoi eliminarli?",
                        PrimaryButtonText = "Sì",
                        CloseButtonText = "No",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = xamlRoot
                    };
                    if (await residueDlg.ShowAsync() == ContentDialogResult.Primary)
                        await _leftoverScanner.DeleteLeftoversAsync(leftovers).ConfigureAwait(true);
                }
                else if (leftovers.Count > 0)
                {
                    Log.Information(
                        "Residui rilevati ({Count}) ma nessun XamlRoot: eliminazione saltata per sicurezza.",
                        leftovers.Count);
                }

                await ReloadInstalledItemsCoreAsync().ConfigureAwait(true);
                StatusText = _allItems.Count + " app installate";
            }
            catch (OperationCanceledException ex)
            {
                Log.Warning(ex, "Disinstallazione annullata o interrotta: {App}", app.Col0);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Warning(ex, "Disinstallazione non completata (es. UAC annullato): {App}", app.Col0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore durante la disinstallazione: {App}", app.Col0);
            }
            finally
            {
                IsBusy = false;
                UninstallCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>Usato dalla pagina per disabilitare una voce di avvio selezionata.</summary>
        public bool DisableStartupForRow(AppManagerRow? row)
        {
            if (row?.Startup == null) return false;
            return _pm.DisableStartup(row.Startup);
        }
    }
}
