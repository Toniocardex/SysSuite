using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace SysSuite.Core
{
    /// <summary>
    /// Toast notification Windows native.
    /// Richiede che AppNotificationManager.Default.Register() sia stato
    /// chiamato all'avvio (fatto in App.xaml.cs).
    /// </summary>
    public static class ToastHelper
    {
        private static bool _registered = false;

        public static void Register()
        {
            try
            {
                if (_registered) return;
                AppNotificationManager.Default.Register();
                _registered = true;
            }
            catch { /* unpackaged app: notifiche potrebbero non funzionare su Win10 */ }
        }

        public static void Send(string title, string message, string? icon = null)
        {
            try
            {
                if (!_registered) Register();
                var builder = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(message);
                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch { /* silenzioso: le toast sono un plus, non critiche */ }
        }

        public static void SendSuccess(string title, string message) =>
            Send(title, message);
    }
}