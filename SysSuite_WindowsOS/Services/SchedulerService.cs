using System.Diagnostics;

namespace SysSuite.Services
{
    /// <summary>
    /// Pianifica o rimuove un task di pulizia automatica via Windows Task Scheduler.
    /// Non richiede admin — usa la cartella utente del Task Scheduler (\SysSuite).
    /// </summary>
    public static class SchedulerService
    {
        private const string TaskName   = "SysSuiteAutoCleanup";
        private const string TaskFolder = "\\SysSuite\\";
        private const string FullTask   = TaskFolder + TaskName;

        public static bool IsScheduled()
        {
            try
            {
                var p = Run("schtasks", "/Query /TN \"" + FullTask + "\" /FO LIST");
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>Crea o aggiorna il task con la frequenza indicata.</summary>
        /// <param name="frequency">"DAILY", "WEEKLY" o "MONTHLY"</param>
        public static bool Schedule(string frequency = "WEEKLY")
        {
            try
            {
                string exe = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(exe)) return false;

                Remove();

                // Passa --boost per eseguire il One-Click Boost in modalità headless
                string args = "/Create /TN \"" + FullTask + "\" /TR \"\\\"" + exe + "\\\" --boost\" /SC " + frequency + " /ST 03:00 /F";
                var p = Run("schtasks", args);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool Remove()
        {
            try
            {
                var p = Run("schtasks", "/Delete /TN \"" + FullTask + "\" /F");
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static string GetNextRun()
        {
            try
            {
                var p = Run("schtasks", "/Query /TN \"" + FullTask + "\" /FO LIST");
                if (p.ExitCode != 0) return "Non pianificato";
                var lines = p.Output.Split('\n');
                var nextLine = lines.FirstOrDefault(l =>
                    l.Contains("Esecuzione successiva") || l.Contains("Next Run"));
                return nextLine?.Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? "n/d";
            }
            catch { return "n/d"; }
        }

        private static (int ExitCode, string Output) Run(string exe, string args)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(exe, args)
                {
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                }
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }
    }
}
