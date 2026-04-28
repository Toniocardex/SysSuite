// SPDX-License-Identifier: MIT
// shell32: SHEmptyRecycleBin deve girare su thread STA (vedi MTA da Task.Run → E_UNEXPECTED 0x8000FFFF).

using System.IO;
using System.Runtime.InteropServices;

namespace SysSuite.Interop;

/// <remarks>
/// <see href="https://learn.microsoft.com/en-us/windows/win32/api/shell/nf-shell-shemptyrecyclebinw">SHEmptyRecycleBin</see>
/// </remarks>
internal static class Shell32
{
    private const int HresultUnexpected = unchecked((int)0x8000_FFFF);

    internal const uint SherbNoConfirmation = 0x0000_0001;
    internal const uint SherbNoProgressUI = 0x0000_0002;
    internal const uint SherbNoSound = 0x0000_0004;

    /// <summary>
    /// Svuota i Cestino senza UI. Esegue l'API su thread <b>STA</b> dedicato (evita E_UNEXPECTED da thread pool MTA).
    /// </summary>
    internal static void EmptyAllRecycleBinsSilently()
    {
        Exception? caught = null;

        var thread = new Thread(() =>
        {
            try
            {
                InvokeEmptyRecycleBin();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Name = "SysSuite.EmptyRecycleBin";
        thread.Start();
        thread.Join();

        if (caught != null)
            throw caught;
    }

    /// <summary>Chiamata su thread STA: prima tutte le unità (pszRoot=null), poi solo volume sistema se E_UNEXPECTED.</summary>
    private static void InvokeEmptyRecycleBin()
    {
        uint flags = SherbNoConfirmation | SherbNoProgressUI | SherbNoSound;
        int hr = SHEmptyRecycleBin(nint.Zero, null, flags);

        if (hr != 0 && hr == HresultUnexpected)
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            hr = SHEmptyRecycleBin(nint.Zero, root, flags);
        }

        Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(nint hwnd, string? pszRootPath, uint dwFlags);
}
