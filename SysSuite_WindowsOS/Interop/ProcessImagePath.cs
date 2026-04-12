using System.Runtime.InteropServices;
using System.Text;

namespace SysSuite.Interop
{
    /// <summary>Percorso eseguibile con <c>OpenProcess</c> + <c>QueryFullProcessImageName</c> (più economico di MainModule).</summary>
    internal static class ProcessImagePath
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint OpenProcess(uint desiredAccess, int inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CloseHandle(nint hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageNameW(
            nint hProcess,
            uint dwFlags,
            StringBuilder lpExeName,
            ref int lpdwSize);

        internal static string TryQuery(int processId)
        {
            if (processId <= 0)
                return "";

            nint h = OpenProcess(ProcessQueryLimitedInformation, 0, processId);
            if (h == 0)
                return "";

            try
            {
                var sb = new StringBuilder(4096);
                int sz = sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref sz) ? sb.ToString() : "";
            }
            finally
            {
                _ = CloseHandle(h);
            }
        }
    }
}
