using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Core;
using SysSuite.Services;

namespace SysSuite.Views
{
    public sealed partial class PrivacyPage : UserControl
    {
        /// <summary>Inoltra messaggi di log verso la finestra host (es. MainSuitePage).</summary>
        public event Action<string, string>? Log;

        private readonly PrivacyService _priv = new();
        private bool _privacyLoading;

        public PrivacyPage()
        {
            InitializeComponent();
            _priv.Log += (msg, type) => Log?.Invoke(msg, type);
            Loaded += (_, _) => LoadPrivacyState();
        }

        private void LoadPrivacyState()
        {
            _privacyLoading = true;
            try
            {
                TogTelemetry.IsOn = !_priv.IsTelemetryDisabled();
                TogAdId.IsOn = !_priv.IsAdvertisingIdDisabled();
                TogActivity.IsOn = !_priv.IsActivityHistoryDisabled();
                TogCortana.IsOn = !_priv.IsCortanaDisabled();
                TogStartSugg.IsOn = !_priv.IsStartSuggestionsDisabled();
                TogLockTips.IsOn = !_priv.IsLockScreenTipsDisabled();
            }
            catch { }
            finally { _privacyLoading = false; }
        }

        private static bool IsTelemetrySection(string tag) =>
            tag is "telemetry" or "adid" or "activity";

        private async void TogPrivacy_Toggled(object sender, RoutedEventArgs e)
        {
            if (_privacyLoading) return;
            if (sender is not ToggleSwitch tog || tog.Tag is not string tag) return;

            bool needsAdmin = tag is "telemetry" or "activity" or "cortana";
            if (needsAdmin && !AdminHelper.IsAdmin())
            {
                Log?.Invoke("Questa impostazione richiede Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
                _privacyLoading = true;
                tog.IsOn = !tog.IsOn;
                _privacyLoading = false;
                return;
            }

            ProgressRing ring = IsTelemetrySection(tag) ? RingTelemetry : RingAssist;
            tog.IsEnabled = false;
            ring.IsActive = true;
            ring.Visibility = Visibility.Visible;
            try
            {
                switch (tag)
                {
                    case "telemetry":
                        await _priv.SetTelemetryAsync(tog.IsOn);
                        break;
                    case "adid":
                        await _priv.SetAdvertisingIdAsync(tog.IsOn);
                        break;
                    case "activity":
                        await _priv.SetActivityHistoryAsync(tog.IsOn);
                        break;
                    case "cortana":
                        await _priv.SetCortanaAsync(tog.IsOn);
                        break;
                    case "startsugg":
                        await _priv.SetStartSuggestionsAsync(tog.IsOn);
                        break;
                    case "locktips":
                        await _priv.SetLockScreenTipsAsync(tog.IsOn);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke("Errore: " + ex.Message, "err");
                _privacyLoading = true;
                tog.IsOn = !tog.IsOn;
                _privacyLoading = false;
            }
            finally
            {
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                tog.IsEnabled = true;
            }
        }
    }
}
