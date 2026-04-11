using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SysSuite.Core;
using SysSuite.Services;
using Windows.UI;

namespace SysSuite.Views
{
    public sealed partial class GamingPage : Page
    {
        private readonly GamingService _gaming = new();

        public GamingPage()
        {
            InitializeComponent();
            _gaming.Log += AppendLog;

            // Ripristina stato Gaming Mode salvato
            Loaded += (_, _) =>
            {
                var s = SettingsService.Load();
                if (s.GamingModeActive)
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 52, 211, 153));
                    TxtStatus.Text = "Gaming Mode ATTIVA";
                    BtnDeactivate.IsEnabled = true;
                    BtnActivate.IsEnabled   = false;
                }
            };

            // Se non admin, mostra avviso nello stato
            if (!AdminHelper.IsAdmin())
                AppendLog("Gaming Mode richiede privilegi Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!CheckAdmin("Attiva Gaming Mode")) return;
            btn.IsEnabled = false;
            BtnDeactivate.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try
            {
                await _gaming.ActivateAsync();
                StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 52, 211, 153));
                TxtStatus.Text = "Gaming Mode ATTIVA";
                BtnDeactivate.IsEnabled = true;
                ToastHelper.SendSuccess("SysSuite One", "Gaming Mode attivata — sistema ottimizzato.");
                SettingsService.Update(s => s.GamingModeActive = true);
            }
            catch (Exception ex)
            {
                AppendLog("Errore: " + ex.Message, "err");
                BtnActivate.IsEnabled = true;
                BtnDeactivate.IsEnabled = false;
            }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnDeactivate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!CheckAdmin("Disattiva Gaming Mode")) return;
            btn.IsEnabled = false;
            BtnActivate.IsEnabled = false;
            BusyRing.Visibility = Visibility.Visible;
            BusyRing.IsActive = true;
            try
            {
                await _gaming.DeactivateAsync();
                StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 90, 90));
                TxtStatus.Text = "Gaming Mode INATTIVA";
                BtnActivate.IsEnabled = true;
                ToastHelper.SendSuccess("SysSuite One", "Gaming Mode disattivata — sistema ripristinato.");
                SettingsService.Update(s => s.GamingModeActive = false);
            }
            catch (Exception ex)
            {
                AppendLog("Errore: " + ex.Message, "err");
                BtnDeactivate.IsEnabled = true;
                BtnActivate.IsEnabled = false;
            }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLog.Text = "";

        private void AppendLog(string msg, string type) =>
            DispatcherQueue.TryEnqueue(() =>
                TxtLog.Text += "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n");

        private bool CheckAdmin(string label)
        {
            if (AdminHelper.IsAdmin()) return true;
            AppendLog("'" + label + "' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
            return false;
        }
    }
}
