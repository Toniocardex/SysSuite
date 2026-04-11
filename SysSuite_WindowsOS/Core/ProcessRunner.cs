using System.Diagnostics;

namespace SysSuite.Core
{
    /// <summary>
    /// Helper condiviso per eseguire processi esterni senza finestra.
    /// Sostituisce i metodi Run() duplicati in 5 servizi.
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>Esegue un processo in background, attende la fine, nessuna finestra.</summary>
        public static void Run(string fileName, string arguments)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            p.Start();
            p.WaitForExit();
        }

        /// <summary>Esegue un processo visibile (es. SFC, DISM che mostrano progressi).</summary>
        public static void RunVisible(string fileName, string arguments)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = true
                }
            };
            p.Start();
            p.WaitForExit();
        }

        /// <summary>Esegue e cattura l'output standard.</summary>
        public static (int ExitCode, string Output) RunCapture(string fileName, string arguments)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }
    }
}
