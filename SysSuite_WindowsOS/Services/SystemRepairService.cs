using SysSuite.Core;

namespace SysSuite.Services
{
    /// <summary>SFC, DISM, CHKDSK — integrità file di sistema.</summary>
    public class SystemRepairService
    {
        public event Action<string,string>? Log;

        public async Task RunDISMAsync(CancellationToken cancellationToken = default)
        {
            Emit("Avvio DISM /RestoreHealth...", "head");
            await ProcessRunner.RunVisibleAsync("DISM.exe", "/Online /Cleanup-Image /RestoreHealth", cancellationToken).ConfigureAwait(false);
            Emit("DISM completato", "ok");
        }

        public async Task RunSFCAsync(CancellationToken cancellationToken = default)
        {
            Emit("Avvio SFC /scannow...", "head");
            await ProcessRunner.RunVisibleAsync("sfc.exe", "/scannow", cancellationToken).ConfigureAwait(false);
            Emit("SFC completato — log: %windir%\\Logs\\CBS\\CBS.log", "ok");
        }

        public async Task ScheduleChkDskAsync(CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAsync("cmd.exe", "/c echo y | chkdsk C: /f /r /x", cancellationToken).ConfigureAwait(false);
            Emit("CHKDSK programmato al prossimo avvio", "ok");
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}
