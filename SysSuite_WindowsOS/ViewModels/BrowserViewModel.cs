using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;
using SysSuite.Core;
using SysSuite.Services;
using Windows.UI;

namespace SysSuite.ViewModels
{
    public partial class BrowserViewModel : ObservableObject
    {
        private readonly BrowserService _svc;
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("BrowserViewModel must be created on the UI thread.");

        private static readonly SolidColorBrush InstalledOkBrush =
            new(Color.FromArgb(255, 52, 211, 153));
        private static readonly SolidColorBrush InstalledMissingBrush =
            new(Color.FromArgb(255, 61, 77, 102));

        public BrowserViewModel(BrowserService svc)
        {
            _svc = svc;
            _dispatcher.TryEnqueue(() => _ = DetectBrowsersAsync());
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BusyRingVisibility))]
        private bool _isBusy;

        [ObservableProperty] private string _logText = "Clicca 'Rileva browser' per iniziare.\n";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ErrorBannerVisibility))]
        private string _errorMessage = "";

        [ObservableProperty]
        private ObservableCollection<BrowserRowUi> _browserRows = new();

        public Visibility BusyRingVisibility =>
            IsBusy ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ErrorBannerVisibility =>
            string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

        partial void OnIsBusyChanged(bool value)
        {
            DetectBrowsersCommand.NotifyCanExecuteChanged();
            CleanCacheAllCommand.NotifyCanExecuteChanged();
            CleanDeepAllCommand.NotifyCanExecuteChanged();
        }

        private bool CanRunWhenIdle() => !IsBusy;

        private IProgress<(string Message, string Type)> CreateServiceProgress() =>
            new Progress<(string Message, string Type)>(pair =>
                _dispatcher.TryEnqueue(() => AppendLogLine(pair.Message, pair.Type)));

        private void AppendLogLine(string msg, string type)
        {
            LogText += "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n";
        }

        private void ClearError() => ErrorMessage = "";

        [RelayCommand(CanExecute = nameof(CanRunWhenIdle))]
        private async Task DetectBrowsersAsync()
        {
            ClearError();
            IsBusy = true;
            try
            {
                var list = await _svc.DetectBrowsersAsync().ConfigureAwait(true);
                BrowserRows.Clear();
                foreach (var b in list)
                {
                    long bytes = b.Installed ? await _svc.GetCacheSizeAsync(b).ConfigureAwait(true) : 0;
                    string sizeStr = bytes == 0 ? "—"
                        : bytes >= 1_048_576 ? bytes / 1_048_576 + " MB"
                        : bytes / 1_024 + " KB";
                    BrowserRows.Add(new BrowserRowUi
                    {
                        Name = b.Name,
                        CachePath = b.CachePath,
                        CacheSize = sizeStr,
                        InstalledText = b.Installed ? "RILEVATO" : "non trovato",
                        InstalledColor = b.Installed ? InstalledOkBrush : InstalledMissingBrush,
                        Info = b
                    });
                }

                AppendLogLine(list.Count(x => x.Installed) + " browser rilevati.", "ok");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore rilevamento browser");
                ErrorMessage = "Rilevamento non riuscito: " + ex.Message;
                AppendLogLine("Rilevamento: " + ex.Message, "err");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanRunWhenIdle))]
        private async Task CleanCacheAllAsync()
        {
            ClearError();
            IsBusy = true;
            var progress = CreateServiceProgress();
            try
            {
                foreach (var row in BrowserRows.Where(r => r.Info.Installed))
                {
                    try
                    {
                        await _svc.CleanCacheAsync(row.Info, progress).ConfigureAwait(true);
                    }
                    catch (BrowserRunningException ex)
                    {
                        string msg = "Impossibile pulire " + ex.BrowserName + ". Chiudilo e riprova.";
                        ErrorMessage = msg;
                        AppendLogLine(ex.BrowserName + ": browser in esecuzione — operazione saltata.", "warn");
                        try
                        {
                            ToastHelper.Send("Pulizia browser", msg);
                        }
                        catch (Exception toastEx)
                        {
                            Serilog.Log.Debug(toastEx, "Toast non mostrato");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Errore pulizia cache browser {Browser}", row.Name);
                        ErrorMessage = "Errore su " + row.Name + ": " + ex.Message;
                        AppendLogLine(row.Name + ": " + ex.Message, "err");
                    }
                }

                await RefreshSizesOnlyAsync().ConfigureAwait(true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanRunWhenIdle))]
        private async Task CleanDeepAllAsync()
        {
            ClearError();
            IsBusy = true;
            var progress = CreateServiceProgress();
            try
            {
                foreach (var row in BrowserRows.Where(r => r.Info.Installed))
                {
                    try
                    {
                        await _svc.CleanAllAsync(row.Info, progress).ConfigureAwait(true);
                    }
                    catch (BrowserRunningException ex)
                    {
                        string msg = "Impossibile pulire " + ex.BrowserName + ". Chiudilo e riprova.";
                        ErrorMessage = msg;
                        AppendLogLine(ex.BrowserName + ": browser in esecuzione — pulizia profonda saltata.", "warn");
                        try
                        {
                            ToastHelper.Send("Pulizia browser", msg);
                        }
                        catch (Exception toastEx)
                        {
                            Serilog.Log.Debug(toastEx, "Toast non mostrato");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Errore pulizia profonda browser {Browser}", row.Name);
                        ErrorMessage = "Errore su " + row.Name + ": " + ex.Message;
                        AppendLogLine(row.Name + ": " + ex.Message, "err");
                    }
                }

                await RefreshSizesOnlyAsync().ConfigureAwait(true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshSizesOnlyAsync()
        {
            foreach (var row in BrowserRows.Where(r => r.Info.Installed))
            {
                long bytes = await _svc.GetCacheSizeAsync(row.Info).ConfigureAwait(true);
                string sizeStr = bytes == 0 ? "—"
                    : bytes >= 1_048_576 ? bytes / 1_048_576 + " MB"
                    : bytes / 1_024 + " KB";
                row.CacheSize = sizeStr;
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            LogText = "";
            AppendLogLine("Log svuotato.", "ok");
        }
    }
}
