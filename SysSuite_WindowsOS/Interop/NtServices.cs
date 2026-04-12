// SPDX-License-Identifier: MIT
// Service Control Manager (advapi32) — enumerazione nativa.

using System.Runtime.InteropServices;

namespace SysSuite.Interop
{
    internal static unsafe class NtServices
    {
        internal const uint ScManagerEnumerateService = 0x0004;
        internal const int ScEnumProcessInfo = 0;

        internal const uint ServiceTypeWin32 = 0x00000030;
        internal const uint ServiceTypeDriver = 0x0000000B;
        internal const uint ServiceStateAll = 0x00000003;

        internal const uint ErrorMoreData = 234;

        /// <summary>Buffer massimo supportato da <see cref="EnumServicesStatusExW"/> (256 KiB).</summary>
        internal const uint MaxEnumBufferBytes = 256 * 1024;

        [StructLayout(LayoutKind.Sequential)]
        internal struct ServiceStatusProcess
        {
            internal uint dwServiceType;
            internal uint dwCurrentState;
            internal uint dwControlsAccepted;
            internal uint dwWin32ExitCode;
            internal uint dwServiceSpecificExitCode;
            internal uint dwCheckPoint;
            internal uint dwWaitHint;
            internal uint dwProcessId;
            internal uint dwServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EnumServiceStatusProcess
        {
            internal nint lpServiceName;
            internal nint lpDisplayName;
            internal ServiceStatusProcess ServiceStatusProcess;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenSCManagerW")]
        internal static extern nint OpenSCManager(nint machineName, nint databaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool CloseServiceHandle(nint hSCObject);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool EnumServicesStatusExW(
            nint hSCManager,
            int infoLevel,
            uint dwServiceType,
            uint dwServiceState,
            byte* lpServices,
            uint cbBufSize,
            out uint pcbBytesNeeded,
            out uint lpServicesReturned,
            ref uint lpResumeHandle,
            nint pszGroupName);
    }
}
