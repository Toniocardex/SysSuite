using System.Diagnostics;
using System.Security.Principal;

namespace SysSuite.Core
{
    /// <summary>
    /// Gestione privilegi amministrativi.
    /// L'app gira normalmente come utente standard;
    /// le operazioni privilegiate controllano IsAdmin() e,
    /// se necessario, offrono il riavvio elevato.
    /// </summary>
    public static class AdminHelper
    {
        private static bool? _cachedIsAdmin;

        /// <summary>Restituisce true se il processo corrente ha privilegi admin.</summary>
        public static bool IsAdmin()
        {
            if (_cachedIsAdmin.HasValue) return _cachedIsAdmin.Value;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal      = new WindowsPrincipal(identity);
                _cachedIsAdmin     = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { _cachedIsAdmin = false; }
            return _cachedIsAdmin.Value;
        }

        /// <summary>
        /// Riavvia l'applicazione con privilegi di Amministratore (UAC).
        /// Il processo corrente viene chiuso dopo il relaunch.
        /// </summary>
        public static void RelaunchAsAdmin()
        {
            try
            {
                var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exe)) return;

                Process.Start(new ProcessStartInfo
                {
                    FileName        = exe,
                    Verb            = "runas",
                    UseShellExecute = true
                });

                // Chiude l'istanza corrente (non elevata)
                Microsoft.UI.Xaml.Application.Current.Exit();
            }
            catch
            {
                // L'utente ha annullato il prompt UAC — non fare nulla
            }
        }
    }
}