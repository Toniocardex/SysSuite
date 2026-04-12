// SPDX-License-Identifier: MIT
// P/Invoke advapi32: enumerazione registro senza Microsoft.Win32.Registry.
// Nota: HKCU\...\Run espone *valori* (nome + comando); l'enumerazione corretta è RegEnumValueEx.
//       RegEnumKeyEx è esposta per sotto-chiavi (estensioni / altri moduli).

using System.Runtime.InteropServices;

namespace SysSuite.Interop
{
    internal static unsafe class NtRegistry
    {
        internal const int ErrorSuccess = 0;
        internal const int ErrorMoreData = 234;
        internal const int ErrorNoMoreItems = 259;
        internal const int ErrorAccessDenied = 5;
        internal const int ErrorFileNotFound = 2;

        internal const uint RegNone = 0;
        internal const uint RegSz = 1;
        internal const uint RegExpandSz = 2;
        internal const uint RegMultiSz = 7;

        internal const uint KeyRead = 0x20019;

        internal static readonly nint HkeyCurrentUser = new(unchecked((int)0x80000001));
        internal static readonly nint HkeyLocalMachine = new(unchecked((int)0x80000002));

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegOpenKeyExW(
            nint hKey,
            char* lpSubKey,
            int ulOptions,
            uint samDesired,
            nint* phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern int RegCloseKey(nint hKey);

        /// <summary>Enumera i nomi delle sotto-chiavi (buffer caratteri UTF-16; lpcchName include il terminatore in ingresso).</summary>
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegEnumKeyExW(
            nint hKey,
            uint dwIndex,
            char* lpName,
            ref uint lpcchName,
            nint lpReserved,
            nint lpClass,
            nint lpcchClass,
            nint lpftLastWriteTime);

        /// <summary>Enumera i nomi e i dati dei valori (Run / RunOnce).</summary>
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegEnumValueW(
            nint hKey,
            uint dwIndex,
            char* lpValueName,
            ref uint lpcchValueName,
            nint lpReserved,
            out uint lpType,
            byte* lpData,
            ref uint lpcbData);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int RegQueryValueExW(
            nint hKey,
            char* lpValueName,
            nint lpReserved,
            uint* lpType,
            byte* lpData,
            ref uint lpcbData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint ExpandEnvironmentStringsW(char* lpSrc, char* lpDst, uint nSize);
    }
}
