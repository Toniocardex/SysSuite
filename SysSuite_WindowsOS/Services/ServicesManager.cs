using System.ServiceProcess;

namespace SysSuite.Services
{
    /// <summary>Abilita, disabilita e controlla lo stato dei servizi Windows.</summary>
    public class ServicesManager
    {
        public event Action<string,string>? Log;

        // Servizi sicuri da disabilitare con descrizione
        public static readonly Dictionary<string,string> SafeToDisable = new()
        {
            ["Fax"]            = "Servizio fax — inutile senza fax fisico",
            ["RemoteRegistry"] = "Accesso remoto al registro — rischio sicurezza",
            ["DiagTrack"]      = "Telemetria Microsoft — invia dati a Microsoft",
            ["lfsvc"]          = "Geolocalizzazione",
            ["XblAuthManager"] = "Xbox Live Auth — inutile senza Xbox",
            ["XblGameSave"]    = "Xbox Game Save",
            ["XboxNetApiSvc"]  = "Xbox Network",
            ["XboxGipSvc"]     = "Xbox Input",
            ["WSearch"]        = "Windows Search (usa CPU, utile solo su HDD)",
        };

        public void Disable(string serviceName)
        {
            try
            {
                using var svc = new ServiceController(serviceName);
                if (svc.Status != ServiceControllerStatus.Stopped)
                    svc.Stop();
                SetStartMode(serviceName, "disabled");
                Emit($"Disabilitato: {serviceName}", "ok");
            }
            catch (Exception ex) { Emit($"Errore {serviceName}: {ex.Message}", "warn"); }
        }

        public void Enable(string serviceName, string startMode = "auto")
        {
            try
            {
                SetStartMode(serviceName, startMode);
                using var svc = new ServiceController(serviceName);
                if (svc.Status != ServiceControllerStatus.Running)
                    svc.Start();
                Emit($"Abilitato: {serviceName}", "ok");
            }
            catch (Exception ex) { Emit($"Errore {serviceName}: {ex.Message}", "warn"); }
        }

        public ServiceControllerStatus GetStatus(string serviceName)
        {
            try
            {
                using var svc = new ServiceController(serviceName);
                return svc.Status;
            }
            catch { return ServiceControllerStatus.Stopped; }
        }

        public void Restart(string serviceName)
        {
            Disable(serviceName);
            Thread.Sleep(500);
            Enable(serviceName);
        }

        private static void SetStartMode(string name, string mode)
        {
            var p = new System.Diagnostics.Process
            {
                StartInfo = new("sc.exe", $"config {name} start= {mode}")
                    { CreateNoWindow = true, UseShellExecute = false }
            };
            p.Start(); p.WaitForExit();
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}