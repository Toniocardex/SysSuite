using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

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
            using var p = NewHiddenProcess(fileName, arguments);
            p.Start();
            p.WaitForExit();
        }

        /// <summary>Esegue un processo visibile (es. SFC, DISM che mostrano progressi).</summary>
        public static void RunVisible(string fileName, string arguments)
        {
            using var p = NewVisibleProcess(fileName, arguments);
            p.Start();
            p.WaitForExit();
        }

        /// <summary>Esegue e cattura l'output standard.
        /// Legge stdout e stderr su thread separati per evitare deadlock
        /// quando il processo produce output su entrambi i pipe.</summary>
        public static (int ExitCode, string Output) RunCapture(string fileName, string arguments)
        {
            using var p = NewCaptureProcess(fileName, arguments);
            p.Start();
            // Lettura parallela obbligatoria: leggere prima stdout poi stderr in sequenza
            // causa deadlock se il buffer di stderr si riempie mentre siamo bloccati su stdout.
            var stdoutTask = Task.Run(() => p.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => p.StandardError.ReadToEnd());
            p.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            return (p.ExitCode, stdoutTask.GetAwaiter().GetResult());
        }

        /// <summary>Versione asincrona di <see cref="Run"/> — non blocca il thread UI.</summary>
        public static async Task RunAsync(string fileName, string arguments,
            CancellationToken cancellationToken = default)
        {
            using var p = NewHiddenProcess(fileName, arguments);
            p.Start();
            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (p.ExitCode != 0)
                Log.Warning("Processo terminato con codice {ExitCode}: {FileName} {Arguments}", p.ExitCode, fileName, arguments);
        }

        /// <summary>Versione asincrona di <see cref="RunVisible"/>.</summary>
        public static async Task RunVisibleAsync(string fileName, string arguments,
            CancellationToken cancellationToken = default)
        {
            using var p = NewVisibleProcess(fileName, arguments);
            p.Start();
            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Versione asincrona di <see cref="RunCapture"/> — legge stdout/stderr in parallelo per evitare deadlock su buffer pieni.</summary>
        public static async Task<(int ExitCode, string Output)> RunCaptureAsync(string fileName, string arguments,
            CancellationToken cancellationToken = default)
        {
            using var p = NewCaptureProcess(fileName, arguments);
            p.Start();
            Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = p.StandardError.ReadToEndAsync(cancellationToken);
            Task exitTask = p.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
            int exit = p.ExitCode;
            if (exit != 0)
                Log.Warning("Processo terminato con codice {ExitCode}: {FileName} {Arguments}", exit, fileName, arguments);
            return (exit, await stdoutTask.ConfigureAwait(false));
        }

        private static Process NewHiddenProcess(string fileName, string arguments) =>
            new()
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };

        private static Process NewVisibleProcess(string fileName, string arguments) =>
            new()
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = true
                }
            };

        private static Process NewCaptureProcess(string fileName, string arguments) =>
            new()
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
    }
}
