using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Core;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Diagnostics;
using SysSuite.Services;
using System.ServiceProcess;

namespace SysSuite.Views
{
    public sealed partial class MainSuitePage : Page
    {
        private readonly CleanupService      _clean   = new();
        private readonly SystemRepairService _repair  = new();
        private readonly PerformanceService  _perf    = new();
        private readonly PrivacyService      _priv    = new();
        private readonly ReportService       _report  = new();
        private readonly ServicesManager    _services = new();

        private readonly UIElement[] _tabs;
        private readonly Button[]    _tabBtns;
        private int _currentTab = 0;

        public MainSuitePage()
        {
            InitializeComponent();
            _clean.Log    += AppendLog;
            _repair.Log   += AppendLog;
            _perf.Log     += AppendLog;
            _priv.Log     += AppendLog;
            _services.Log += AppendLog;

            _tabs    = new UIElement[] { TabPulizia, TabPrestazioni, TabPrivacy, TabServizi, TabReport };
            _tabBtns = new Button[]    { BtnTab0, BtnTab1, BtnTab2, BtnTab3, BtnTab4 };

            // Ripristina ultima tab visitata
            Loaded += (_, _) =>
            {
                var s = SettingsService.Load();
                int startTab = s.LastOptimizationTab;
                SwitchTab(Math.Clamp(startTab, 0, _tabs.Length - 1));
                LoadServices();
                RefreshCurrentPlan();
                RefreshScheduleStatus();
                LoadPrivacyState();
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
        private void BtnCleanRecycle_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Svuota Cestino")) return;
            try { _clean.CleanRecycleBin(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnCleanWU_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Cache Windows Update")) return;
            try { _clean.CleanWindowsUpdate(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
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

            BtnCleanTemp_Click(s, e);
            BtnCleanThumb_Click(s, e);
            BtnCleanWinTemp_Click(s, e);
            BtnCleanPrefetch_Click(s, e);
            BtnCleanRecycle_Click(s, e);
            BtnCleanWU_Click(s, e);
            ToastHelper.SendSuccess("SysSuite One", "Pulizia completata.");
        }

        // ── Riparazione — sempre admin ─────────────────────────
        private void BtnSFC_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("SFC /scannow")) return;
            try { _repair.RunSFC(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnDISM_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("DISM RestoreHealth")) return;
            try { _repair.RunDISM(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnCHKDSK_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Pianifica CHKDSK")) return;
            try { _repair.ScheduleChkDsk(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

        // ── Prestazioni ────────────────────────────────────────
        // Piani energetici: powercfg — admin richiesto
        private void BtnBalanced_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Piano Bilanciato")) return;
            try { _perf.SetBalancedPlan(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnUltimate_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Piano Ultimate Performance")) return;
            try { _perf.SetUltimatePlan(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
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
        private void BtnDefrag_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Ottimizza disco")) return;
            try { _perf.OptimizeDisk(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

        // ── Privacy — ToggleSwitch bidirezionali ──────────────────
        private bool _privacyLoading = false;

        private void LoadPrivacyState()
        {
            _privacyLoading = true;
            try
            {
                TogTelemetry.IsOn = !_priv.IsTelemetryDisabled();
                TogAdId.IsOn      = !_priv.IsAdvertisingIdDisabled();
                TogActivity.IsOn  = !_priv.IsActivityHistoryDisabled();
                TogCortana.IsOn   = !_priv.IsCortanaDisabled();
                TogStartSugg.IsOn = !_priv.IsStartSuggestionsDisabled();
                TogLockTips.IsOn  = !_priv.IsLockScreenTipsDisabled();
            }
            catch { }
            finally { _privacyLoading = false; }
        }

        private void TogPrivacy_Toggled(object sender, RoutedEventArgs e)
        {
            if (_privacyLoading) return;
            if (sender is not ToggleSwitch tog || tog.Tag is not string tag) return;

            // Telemetria, Cronologia, Cortana richiedono admin (HKLM)
            bool needsAdmin = tag is "telemetry" or "activity" or "cortana";
            if (needsAdmin && !AdminHelper.IsAdmin())
            {
                AppendLog("Questa impostazione richiede Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
                _privacyLoading = true;
                tog.IsOn = !tog.IsOn; // revert
                _privacyLoading = false;
                return;
            }

            try
            {
                switch (tag)
                {
                    case "telemetry": _priv.SetTelemetry(tog.IsOn); break;
                    case "adid":     _priv.SetAdvertisingId(tog.IsOn); break;
                    case "activity": _priv.SetActivityHistory(tog.IsOn); break;
                    case "cortana":  _priv.SetCortana(tog.IsOn); break;
                    case "startsugg":_priv.SetStartSuggestions(tog.IsOn); break;
                    case "locktips": _priv.SetLockScreenTips(tog.IsOn); break;
                }
            }
            catch (Exception ex)
            {
                AppendLog("Errore: " + ex.Message, "err");
                _privacyLoading = true;
                tog.IsOn = !tog.IsOn;
                _privacyLoading = false;
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
        private void LoadServices()
        {
            try
            {
                LvServices.ItemsSource = ServicesManager.SafeToDisable
                    .Select(kv =>
                    {
                        var status = _services.GetStatus(kv.Key);
                        bool running = status == System.ServiceProcess.ServiceControllerStatus.Running;
                        return new
                        {
                            Name        = kv.Key,
                            Description = kv.Value,
                            Status      = running ? "In esecuzione" : "Fermato",
                            StatusColor = running
                                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                    Windows.UI.Color.FromArgb(255, 255, 90, 90))
                                : new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                    Windows.UI.Color.FromArgb(255, 52, 211, 153))
                        };
                    }).ToList();
            }
            catch (Exception ex) { AppendLog("Servizi: " + ex.Message, "err"); }
        }

        private void BtnSvcRefresh_Click(object s, RoutedEventArgs e) => LoadServices();

        private void BtnSvcDisable_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Disabilita servizio")) return;
            if (LvServices.SelectedIndex < 0) { AppendLog("Seleziona un servizio.", "warn"); return; }
            var name = ServicesManager.SafeToDisable.Keys.ElementAt(LvServices.SelectedIndex);
            try { _services.Disable(name); LoadServices(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

        private void BtnSvcEnable_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Abilita servizio")) return;
            if (LvServices.SelectedIndex < 0) { AppendLog("Seleziona un servizio.", "warn"); return; }
            var name = ServicesManager.SafeToDisable.Keys.ElementAt(LvServices.SelectedIndex);
            try { _services.Enable(name); LoadServices(); }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

                private void RefreshCurrentPlan()
        {
            try
            {
                var plan = _perf.GetCurrentPlan();
                TxtCurrentPlan.Text = "Piano: " + plan;
            }
            catch { TxtCurrentPlan.Text = "Piano: n/d"; }
        }

        private void BtnBalancedEx_Click(object s, RoutedEventArgs e)
        {
            BtnBalanced_Click(s, e);
            RefreshCurrentPlan();
        }

        private void BtnUltimateEx_Click(object s, RoutedEventArgs e)
        {
            BtnUltimate_Click(s, e);
            RefreshCurrentPlan();
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