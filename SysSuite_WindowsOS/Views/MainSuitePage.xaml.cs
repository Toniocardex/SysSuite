using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.Core;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Diagnostics;
using SysSuite.Services;
using SysSuite.ViewModels;
using System.Threading.Tasks;

namespace SysSuite.Views
{
    public sealed partial class MainSuitePage : Page
    {
        private sealed record ServiceUiRow(string Name, string Description, string Status, SolidColorBrush StatusColor);

        private readonly CleanupService      _clean   = new();
        private readonly SystemRepairService _repair  = new();
        private readonly PerformanceService  _perf    = new();
        private readonly ReportService       _report  = new();
        private readonly ServicesManager    _services;
        private readonly WindowsServicesViewModel _servicesVm = new();

        private readonly UIElement[] _tabs;
        private readonly Button[]    _tabBtns;
        private int _currentTab = 0;

        public MainSuitePage()
        {
            InitializeComponent();
            _services = new ServicesManager(App.Services.GetRequiredService<SystemRestoreService>());
            _clean.Log    += AppendLog;
            _repair.Log   += AppendLog;
            _perf.Log     += AppendLog;
            TabPrivacy.Log += AppendLog;
            _services.Log += AppendLog;

            _tabs    = new UIElement[] { TabPulizia, TabPrestazioni, TabPrivacy, TabServizi, TabReport };
            _tabBtns = new Button[]    { BtnTab0, BtnTab1, BtnTab2, BtnTab3, BtnTab4 };

            // Ripristina ultima tab visitata
            Loaded += async (_, _) =>
            {
                // Permette al layout della tab iniziale di completarsi prima di restore/piano energetico.
                await Task.Yield();
                var s = SettingsService.Load();
                int startTab = s.LastOptimizationTab;
                SwitchTab(Math.Clamp(startTab, 0, _tabs.Length - 1));
                await LoadServicesAsync();
                RefreshScheduleStatus();
            };
        }

        private void TabBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int idx))
                SwitchTab(idx);
        }

        private void SwitchTab(int idx)
        {
            if (idx < 0 || idx >= _tabs.Length) return;
            _currentTab = idx;

            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].Visibility    = i == idx ? Visibility.Visible : Visibility.Collapsed;
                _tabBtns[i].Style      = (Microsoft.UI.Xaml.Style)Application.Current.Resources[
                    i == idx ? "SysButtonPrimary" : "SysButtonGhost"];
            }

            // Persiste la tab corrente
            SettingsService.Update(s => s.LastOptimizationTab = idx);

            // Aggiorna piano energetico quando si apre Prestazioni (non solo al primo Loaded della pagina).
            if (idx == 1)
                _ = RefreshCurrentPlanWhenIdleAsync();
        }

        /// <summary>Legge il piano su thread pool e aggiorna la UI senza bloccare il dispatcher.</summary>
        private async Task RefreshCurrentPlanWhenIdleAsync()
        {
            try
            {
                string plan = await Task.Run(() => _perf.GetCurrentPlan()).ConfigureAwait(false);
                DispatcherQueue.TryEnqueue(() => TxtCurrentPlan.Text = "Piano: " + plan);
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() => TxtCurrentPlan.Text = "Piano: n/d");
            }
        }

                // ── Pulizia ────────────────────────────────────────────
        // CleanTemp: %TEMP% utente — NO admin
        private void BtnCleanTemp_Click(object s, RoutedEventArgs e)
        {
            try { _clean.CleanTemp(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        // CleanThumbnails: profilo utente — NO admin
        private void BtnCleanThumb_Click(object s, RoutedEventArgs e)
        {
            try { _clean.CleanThumbnails(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        // Seguenti: C:\Windows\* — richiedono admin
        private void BtnCleanWinTemp_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Pulizia Windows Temp")) return;
            try { _clean.CleanWinTemp(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnCleanPrefetch_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Pulizia Prefetch")) return;
            try { _clean.CleanPrefetch(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private async void BtnCleanRecycle_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("Svuota Cestino")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try { await _clean.CleanRecycleBinAsync(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }
        private async void BtnCleanWU_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("Cache Windows Update")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try { await _clean.CleanWindowsUpdateAsync(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }
        private async void BtnCleanAll_Click(object s, RoutedEventArgs e)
        {
            var confirm = new ContentDialog
            {
                Title             = "Pulizia completa",
                Content           = "Verranno eliminati file temporanei, cache miniature, Prefetch,\n"
                                  + "cestino e cache di Windows Update.\n\n"
                                  + "Alcune operazioni richiedono privilegi amministratore.\nContinuare?",
                PrimaryButtonText = "Avvia pulizia",
                CloseButtonText   = "Annulla",
                XamlRoot          = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            BtnCleanAll.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try
            {
                await Task.Run(() => { try { _clean.CleanTemp(); } catch (Exception ex) { AppendLog(ex.Message, "err"); } });
                await Task.Run(() => { try { _clean.CleanThumbnails(); } catch (Exception ex) { AppendLog(ex.Message, "err"); } });
                if (AdminHelper.IsAdmin())
                {
                    await Task.Run(() => { try { _clean.CleanWinTemp(); } catch (Exception ex) { AppendLog(ex.Message, "err"); } });
                    await Task.Run(() => { try { _clean.CleanPrefetch(); } catch (Exception ex) { AppendLog(ex.Message, "err"); } });
                    try { await _clean.CleanRecycleBinAsync(); }
                    catch (Exception ex) { AppendLog(ex.Message, "err"); }
                    try { await _clean.CleanWindowsUpdateAsync(); }
                    catch (Exception ex) { AppendLog(ex.Message, "err"); }
                }
                else
                    AppendLog("Pulizia completa: operazioni su C:\\Windows, cestino e WU richiedono Admin.", "warn");
                ToastHelper.SendSuccess("SysSuite One", "Pulizia completata.");
            }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                BtnCleanAll.IsEnabled = true;
            }
        }

        // ── Riparazione — sempre admin ─────────────────────────
        private async void BtnSFC_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("SFC /scannow")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try { await _repair.RunSFCAsync(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }
        private async void BtnDISM_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("DISM RestoreHealth")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try { await _repair.RunDISMAsync(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }
        private async void BtnCHKDSK_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("Pianifica CHKDSK")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try { await _repair.ScheduleChkDskAsync(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }

        // ── Prestazioni ────────────────────────────────────────
        // Piani energetici: powercfg — admin richiesto
        private async void BtnBalanced_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("Piano Bilanciato")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try
            {
                await _perf.SetBalancedPlanAsync();
                await RefreshCurrentPlanAsync();
            }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }
        private async void BtnUltimate_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("Piano Ultimate Performance")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try
            {
                await _perf.SetUltimatePlanAsync();
                await RefreshCurrentPlanAsync();
            }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }
        // ReduceAnimations: HKCU — NO admin
        private void BtnAnim_Click(object s, RoutedEventArgs e)
        {
            try { _perf.ReduceAnimations(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        // EnableFastStartup: HKLM — admin
        private void BtnFastBoot_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Avvio rapido")) return;
            try { _perf.EnableFastStartup(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        // OptimizeDisk: defrag — admin
        private async void BtnDefrag_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            if (!CheckAdmin("Ottimizza disco")) return;
            btn.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try { await _perf.OptimizeDiskAsync(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }

        // ── Report — NO admin ──────────────────────────────────
        private void BtnReportTxt_Click(object s, RoutedEventArgs e)
        {
            try { var p = _report.SaveTxt(SystemInfo.Collect()); AppendLog("Report TXT: " + p, "ok"); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnReportHtml_Click(object s, RoutedEventArgs e)
        {
            try { var p = _report.SaveHtml(SystemInfo.Collect()); AppendLog("Report HTML: " + p, "ok"); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

        // ── Servizi Windows ────────────────────────────────────────
        private async Task LoadServicesAsync()
        {
            try
            {
                await _servicesVm.RefreshAsync(_services).ConfigureAwait(true);
                var red = new SolidColorBrush(Color.FromArgb(255, 255, 90, 90));
                var green = new SolidColorBrush(Color.FromArgb(255, 52, 211, 153));
                LvServices.ItemsSource = ServicesManager.SafeToDisable
                    .Select(kv =>
                    {
                        SysSuite.Models.WindowsServiceListItem? row = _servicesVm.Services
                            .FirstOrDefault(x => x.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                        bool running = row?.CurrentState == 4;
                        return new ServiceUiRow(
                            kv.Key,
                            kv.Value,
                            running ? "In esecuzione" : "Fermato",
                            running ? red : green);
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                AppendLog("Servizi: " + ex.Message, "err");
            }
        }

        private async void BtnSvcRefresh_Click(object s, RoutedEventArgs e) => await LoadServicesAsync();

        private async void BtnSvcDisable_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Disabilita servizio")) return;
            if (LvServices.SelectedItem is not ServiceUiRow row)
            {
                AppendLog("Seleziona un servizio.", "warn");
                return;
            }

            try
            {
                await _services.DisableAsync(row.Name).ConfigureAwait(true);
                await LoadServicesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, "err");
            }
        }

        private async void BtnSvcEnable_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Abilita servizio")) return;
            if (LvServices.SelectedItem is not ServiceUiRow row)
            {
                AppendLog("Seleziona un servizio.", "warn");
                return;
            }

            try
            {
                await _services.EnableAsync(row.Name).ConfigureAwait(true);
                await LoadServicesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, "err");
            }
        }

                private async Task RefreshCurrentPlanAsync()
        {
            try
            {
                var plan = await _perf.GetCurrentPlanAsync();
                TxtCurrentPlan.Text = "Piano: " + plan;
            }
            catch { TxtCurrentPlan.Text = "Piano: n/d"; }
        }

        private void BtnAnimRestore_Click(object s, RoutedEventArgs e)
        {
            try { _perf.RestoreAnimations(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

                private void RefreshScheduleStatus()
        {
            try
            {
                if (SchedulerService.IsScheduled())
                {
                    string next = SchedulerService.GetNextRun();
                    TxtScheduleStatus.Text = "Attivo — prossima esecuzione: " + next;
                    TxtScheduleStatus.Foreground =
                        new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Windows.UI.Color.FromArgb(255, 52, 211, 153));
                }
                else
                {
                    TxtScheduleStatus.Text = "Non pianificata";
                    TxtScheduleStatus.Foreground =
                        new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Windows.UI.Color.FromArgb(255, 61, 77, 102));
                }
            }
            catch { }
        }

        private void BtnScheduleWeekly_Click(object s, RoutedEventArgs e)
        {
            bool ok = SchedulerService.Schedule("WEEKLY");
            AppendLog(ok ? "Pulizia settimanale pianificata (ogni lunedi ore 03:00)" : "Errore pianificazione.", ok ? "ok" : "err");
            RefreshScheduleStatus();
        }
        private void BtnScheduleDaily_Click(object s, RoutedEventArgs e)
        {
            bool ok = SchedulerService.Schedule("DAILY");
            AppendLog(ok ? "Pulizia giornaliera pianificata (ore 03:00)" : "Errore pianificazione.", ok ? "ok" : "err");
            RefreshScheduleStatus();
        }
        private void BtnScheduleMonthly_Click(object s, RoutedEventArgs e)
        {
            bool ok = SchedulerService.Schedule("MONTHLY");
            AppendLog(ok ? "Pulizia mensile pianificata (ore 03:00)" : "Errore pianificazione.", ok ? "ok" : "err");
            RefreshScheduleStatus();
        }
        private void BtnScheduleRemove_Click(object s, RoutedEventArgs e)
        {
            bool ok = SchedulerService.Remove();
            AppendLog(ok ? "Pianificazione rimossa." : "Errore rimozione.", ok ? "ok" : "err");
            RefreshScheduleStatus();
        }

        private void LogCopyMenu_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            LogClipboardHelper.CopyToClipboard(TxtLog.Text);

        private void BtnClearLog_Click(object s, RoutedEventArgs e) => TxtLog.Text = "";

        private void AppendLog(string msg, string type) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                string prefix = type switch { "ok" => "[OK] ", "err" => "[ERR] ", "head" => "=== ", "warn" => "[!]  ", _ => "[..] " };
                TxtLog.Text += prefix + msg + "\n";
                // Auto-scroll al fondo del log
                if (LogScroll != null)
                    LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
            });

        private bool CheckAdmin(string label)
        {
            if (AdminHelper.IsAdmin()) return true;
            AppendLog("'" + label + "' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
            return false;
        }
    }
}