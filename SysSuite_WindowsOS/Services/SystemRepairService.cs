using SysSuite.Core;

namespace SysSuite.Services
{
    /// <summary>SFC, DISM, CHKDSK — integrità file di sistema.</summary>
    public class SystemRepairService
    {
        public event Action<string,string>? Log;

        public void RunDISM()
        {
            Emit("Avvio DISM /RestoreHealth...", "head");
            ProcessRunner.RunVisible("DISM.exe", "/Online /Cleanup-Image /RestoreHealth");
            Emit("DISM completato", "ok");
        }

        public void RunSFC()
        {
            Emit("Avvio SFC /scannow...", "head");
            ProcessRunner.RunVisible("sfc.exe", "/scannow");
            Emit("SFC completato — log: %windir%\\Logs\\CBS\\CBS.log", "ok");
        }

        public void ScheduleChkDsk()
        {
            ProcessRunner.Run("cmd.exe", "/c echo y | chkdsk C: /f /r /x");
            Emit("CHKDSK programmato al prossimo avvio", "ok");
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}
