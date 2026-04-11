using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using SysSuite.Core;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    public partial class PrivacyViewModel : ObservableObject
    {
        private readonly PrivacyService _priv;
        private bool _hydrating;

        public event Action<string, string>? DiagnosticLog;

        public PrivacyViewModel(PrivacyService priv)
        {
            _priv = priv;
            _priv.Log += (msg, type) => DiagnosticLog?.Invoke(msg, type);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TelemetryRingVisibility))]
        [NotifyPropertyChangedFor(nameof(ArePrivacyTogglesEnabled))]
        private bool _isTelemetrySectionBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AssistRingVisibility))]
        [NotifyPropertyChangedFor(nameof(ArePrivacyTogglesEnabled))]
        private bool _isAssistSectionBusy;

        [ObservableProperty] private bool _isTelemetryOn;

        [ObservableProperty] private bool _isAdIdOn;

        [ObservableProperty] private bool _isActivityOn;

        [ObservableProperty] private bool _isCortanaOn;

        [ObservableProperty] private bool _isStartSuggOn;

        [ObservableProperty] private bool _isLockTipsOn;

        public Visibility TelemetryRingVisibility =>
            IsTelemetrySectionBusy ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AssistRingVisibility =>
            IsAssistSectionBusy ? Visibility.Visible : Visibility.Collapsed;

        public bool ArePrivacyTogglesEnabled =>
            !IsTelemetrySectionBusy && !IsAssistSectionBusy;

        public void LoadFromRegistry()
        {
            _hydrating = true;
            try
            {
                IsTelemetryOn = !_priv.IsTelemetryDisabled();
                IsAdIdOn = !_priv.IsAdvertisingIdDisabled();
                IsActivityOn = !_priv.IsActivityHistoryDisabled();
                IsCortanaOn = !_priv.IsCortanaDisabled();
                IsStartSuggOn = !_priv.IsStartSuggestionsDisabled();
                IsLockTipsOn = !_priv.IsLockScreenTipsDisabled();
            }
            finally
            {
                _hydrating = false;
            }
        }

        private bool CheckAdmin(string humanLabel)
        {
            if (AdminHelper.IsAdmin()) return true;
            DiagnosticLog?.Invoke(
                "Questa impostazione (" + humanLabel + ") richiede Admin. Usa 'Riavvia come Admin' in basso a destra.",
                "warn");
            return false;
        }

        private void RevertTelemetry(bool toValue)
        {
            _hydrating = true;
            IsTelemetryOn = toValue;
            _hydrating = false;
        }

        private void RevertActivity(bool toValue)
        {
            _hydrating = true;
            IsActivityOn = toValue;
            _hydrating = false;
        }

        private void RevertCortana(bool toValue)
        {
            _hydrating = true;
            IsCortanaOn = toValue;
            _hydrating = false;
        }

        private void RevertAdId(bool toValue)
        {
            _hydrating = true;
            IsAdIdOn = toValue;
            _hydrating = false;
        }

        private void RevertStartSugg(bool toValue)
        {
            _hydrating = true;
            IsStartSuggOn = toValue;
            _hydrating = false;
        }

        private void RevertLockTips(bool toValue)
        {
            _hydrating = true;
            IsLockTipsOn = toValue;
            _hydrating = false;
        }

        partial void OnIsTelemetryOnChanged(bool value)
        {
            if (_hydrating) return;
            if (!CheckAdmin("Telemetria"))
            {
                RevertTelemetry(!value);
                return;
            }

            _ = RunTelemetrySectionAsync(
                async () => await _priv.SetTelemetryAsync(value).ConfigureAwait(true),
                () => RevertTelemetry(!value));
        }

        partial void OnIsAdIdOnChanged(bool value)
        {
            if (_hydrating) return;
            _ = RunTelemetrySectionAsync(
                async () => await _priv.SetAdvertisingIdAsync(value).ConfigureAwait(true),
                () => RevertAdId(!value));
        }

        partial void OnIsActivityOnChanged(bool value)
        {
            if (_hydrating) return;
            if (!CheckAdmin("Cronologia attività"))
            {
                RevertActivity(!value);
                return;
            }

            _ = RunTelemetrySectionAsync(
                async () => await _priv.SetActivityHistoryAsync(value).ConfigureAwait(true),
                () => RevertActivity(!value));
        }

        partial void OnIsCortanaOnChanged(bool value)
        {
            if (_hydrating) return;
            if (!CheckAdmin("Cortana"))
            {
                RevertCortana(!value);
                return;
            }

            _ = RunAssistSectionAsync(
                async () => await _priv.SetCortanaAsync(value).ConfigureAwait(true),
                () => RevertCortana(!value));
        }

        partial void OnIsStartSuggOnChanged(bool value)
        {
            if (_hydrating) return;
            _ = RunAssistSectionAsync(
                async () => await _priv.SetStartSuggestionsAsync(value).ConfigureAwait(true),
                () => RevertStartSugg(!value));
        }

        partial void OnIsLockTipsOnChanged(bool value)
        {
            if (_hydrating) return;
            _ = RunAssistSectionAsync(
                async () => await _priv.SetLockScreenTipsAsync(value).ConfigureAwait(true),
                () => RevertLockTips(!value));
        }

        private async Task RunTelemetrySectionAsync(Func<Task> work, Action revert)
        {
            IsTelemetrySectionBusy = true;
            try
            {
                await work().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticLog?.Invoke("Errore: " + ex.Message, "err");
                revert();
            }
            finally
            {
                IsTelemetrySectionBusy = false;
            }
        }

        private async Task RunAssistSectionAsync(Func<Task> work, Action revert)
        {
            IsAssistSectionBusy = true;
            try
            {
                await work().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticLog?.Invoke("Errore: " + ex.Message, "err");
                revert();
            }
            finally
            {
                IsAssistSectionBusy = false;
            }
        }
    }
}
