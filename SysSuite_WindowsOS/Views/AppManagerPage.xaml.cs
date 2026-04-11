using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.Views
{
    public sealed partial class AppManagerPage : Page
    {
        private readonly ProcessManager _pm = new();
        private List<StartupEntry> _startupEntries = new();
        private System.Collections.IList? _fullList;
        private string _mode = "";

        public AppManagerPage() { InitializeComponent(); }


        /// <summary>Aggiorna le intestazioni colonna in base alla vista corrente.</summary>
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_fullList == null) return;
            var q = TxtSearch.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q))
            {
                LvItems.ItemsSource = _fullList;
                TxtCount.Text = _fullList.Count + " elementi";
                return;
            }
            var filtered = _fullList.Cast<object>()
                .Where(item =>
                {
                    var c0 = item.GetType().GetProperty("Col0")?.GetValue(item)?.ToString() ?? "";
                    var c1 = item.GetType().GetProperty("Col1")?.GetValue(item)?.ToString() ?? "";
                    var c2 = item.GetType().GetProperty("Col2")?.GetValue(item)?.ToString() ?? "";
                    return c0.ToLowerInvariant().Contains(q)
                        || c1.ToLowerInvariant().Contains(q)
                        || c2.ToLowerInvariant().Contains(q);
                }).ToList();
            LvItems.ItemsSource = filtered;
            TxtCount.Text = filtered.Count + " risultati";
        }

                private void SetHeaders(string col0, string col1, string col2)
        {
            TxtH0.Text = col0;
            TxtH1.Text = col1;
            TxtH2.Text = col2;
        }
        private void BtnStartup_Click(object s, RoutedEventArgs e)
        {
            _mode = "startup";
            SetHeaders("NOME", "ORIGINE", "COMANDO");
            _startupEntries = _pm.GetStartupEntries();
            var list0 = _startupEntries.Select(x => new
            {
                Col0 = x.Name, Col1 = x.Source, Col2 = x.Command
            }).ToList();
            _fullList = list0;
            LvItems.ItemsSource = list0;
            TxtCount.Text = _startupEntries.Count + " voci avvio";
        }

        private void BtnInstalled_Click(object s, RoutedEventArgs e)
        {
            _mode = "apps";
            SetHeaders("NOME APP", "VERSIONE", "PRODUTTORE");
            var apps = _pm.GetInstalledApps();
            var list1 = apps.Select(a => new
            {
                Col0 = a.Name, Col1 = a.Version, Col2 = a.Publisher
            }).ToList();
            _fullList = list1;
            LvItems.ItemsSource = list1;
            TxtCount.Text = apps.Count + " app installate";
        }

        // DisableStartup: per HKLM serve admin; per HKCU e cartella avvio no
        private async void BtnUnused_Click(object s, RoutedEventArgs e)
        {
            _mode = "unused";
            SetHeaders("NOME APP", "ULTIMO USO", "PRODUTTORE");
            TxtCount.Text = "Ricerca app inutilizzate...";
            LvItems.ItemsSource = null;
            try
            {
                var apps = await Task.Run(() => _pm.GetUnusedApps(90));
                var list2 = apps.Select(a => new
                {
                    Col0 = a.Name,
                    Col1 = a.DaysUnused + " gg fa",
                    Col2 = a.Publisher
                }).ToList();
                _fullList = list2;
                LvItems.ItemsSource = list2;
                TxtCount.Text = apps.Count + " app non usate da 90+ giorni";
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
        }

        private async void BtnDisable_Click(object s, RoutedEventArgs e)
        {
            if (_mode != "startup" || LvItems.SelectedIndex < 0)
            {
                TxtCount.Text = "Seleziona una voce di avvio.";
                return;
            }
            var entry = _startupEntries[LvItems.SelectedIndex];

            if (entry.Source.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                && !AdminHelper.IsAdmin())
            {
                TxtCount.Text = "Voci HKLM richiedono Admin. Usa 'Riavvia come Admin' in basso a destra.";
                return;
            }

            var confirm = new ContentDialog
            {
                Title             = "Disabilita avvio automatico",
                Content           = "Disabilitare " + entry.Name + " dall'avvio automatico?\n"
                                  + "Origine: " + entry.Source,
                PrimaryButtonText = "Disabilita",
                CloseButtonText   = "Annulla",
                XamlRoot          = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            bool ok = _pm.DisableStartup(entry);
            TxtCount.Text = ok ? "Disabilitato: " + entry.Name : "Errore disabilitazione.";
            BtnStartup_Click(s, e);
        }
    }
}