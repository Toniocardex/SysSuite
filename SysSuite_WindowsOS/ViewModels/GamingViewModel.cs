using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SysSuite.Core;
using SysSuite.Services;
using Windows.UI;

namespace SysSuite.ViewModels
{
    public partial class GamingViewModel : ObservableObject
    {
        private static readonly SolidColorBrush ActiveDot =
            new(Color.FromArgb(255, 52, 211, 153));

        private static readonly SolidColorBrush InactiveDot =
            new(Color.FromArgb(255, 255, 90, 90));

        private readonly GamingService _gaming;
        private readonly PowerOptimizationService _powerOptimization;
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("GamingViewModel must be created on the UI thread.");

        public GamingViewModel(GamingService gaming, PowerOptimizationService powerOptimization)
        {
            _gaming = gaming;
            _powerOptimization = powerOptimization;
            _gaming.Log += OnServiceLog;
            _powerOptimization.Log += OnServiceLog;

            GamingModeActive = SettingsService.Load().GamingModeActive;

            if (!AdminHelper.IsAdmin())
                AppendLogLine("Gaming Mode richiede privilegi Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusHeadline))]
        [NotifyPropertyChangedFor(nameof(StatusDotFill))]
        [NotifyPropertyChangedFor(nameof(IsActivateButtonEnabled))]
        [NotifyPropertyChangedFor(nameof(IsDeactivateButtonEnabled))]
        private bool _gamingModeActive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsActivateButtonEnabled))]
        [NotifyPropertyChangedFor(nameof(IsDeactivateButtonEnabled))]
        [NotifyPropertyChangedFor(nameof(IsOptimizePowerButtonEnabled))]
        [NotifyPropertyChangedFor(nameof(BusyRingVisibility))]
        private bool _isBusy;

        [ObservableProperty] private string _logText =
            "Pronto. Usa i pulsanti sopra per attivare o disattivare Gaming Mode.";

        public string StatusHeadline =>
            GamingModeActive ? "Gaming Mode ATTIVA" : "Gaming Mode INATTIVA";

        public SolidColorBrush StatusDotFill =>
            GamingModeActive ? ActiveDot : InactiveDot;

        public bool IsActivateButtonEnabled => !IsBusy && !GamingModeActive;

        public bool IsDeactivateButtonEnabled => !IsBusy && GamingModeActive;

        public bool IsOptimizePowerButtonEnabled => !IsBusy;

        public Visibility BusyRingVisibility =>
            IsBusy ? Visibility.Visible : Visibility.Collapsed;

        private void OnServiceLog(string msg, string type) =>
            EnqueueUi(() => AppendLogLine(msg, type));

        private void AppendLogLine(string msg, string type)
        {
            LogText += "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n";
        }

        private void EnqueueUi(Action action)
        {
            if (_dispatcher.HasThreadAccess)
                action();
            else
                _ = _dispatcher.TryEnqueue(() => action());
        }

        private static bool CheckAdmin(string label, Action<string, string> log)
        {
            if (AdminHelper.IsAdmin()) return true;
            log("'" + label + "' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
            return false;
        }

        [RelayCommand]
        private async Task OptimizePowerAsync()
        {
            if (!CheckAdmin("Ottimizza potenza", AppendLogLine)) return;

            IsBusy = true;
            try
            {
                await _powerOptimization.OptimizeAsync().ConfigureAwait(true);
                ToastHelper.SendSuccess(
                    "SysSuite One — Potenza",
                    "Piano prestazioni elevato e core parking applicati.");
            }
            catch (Exception ex)
            {
                AppendLogLine("Errore: " + ex.Message, "err");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task ActivateAsync()
        {
            if (!CheckAdmin("Attiva Gaming Mode", AppendLogLine)) return;

            IsBusy = true;
            try
            {
                await _gaming.ActivateAsync().ConfigureAwait(true);
                GamingModeActive = true;
                ToastHelper.SendSuccess("SysSuite One", "Gaming Mode attivata — sistema ottimizzato.");
                SettingsService.Update(s => s.GamingModeActive = true);
            }
            catch (Exception ex)
            {
                AppendLogLine("Errore: " + ex.Message, "err");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task DeactivateAsync()
        {
            if (!CheckAdmin("Disattiva Gaming Mode", AppendLogLine)) return;

            IsBusy = true;
            try
            {
                await _gaming.DeactivateAsync().ConfigureAwait(true);
                GamingModeActive = false;
                ToastHelper.SendSuccess("SysSuite One", "Gaming Mode disattivata — sistema ripristinato.");
                SettingsService.Update(s => s.GamingModeActive = false);
            }
            catch (Exception ex)
            {
                AppendLogLine("Errore: " + ex.Message, "err");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void ClearLog() => LogText = "";
    }
}
