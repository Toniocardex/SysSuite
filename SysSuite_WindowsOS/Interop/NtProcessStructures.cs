// SPDX-License-Identifier: MIT
// Strutture e layout SYSTEM_PROCESS_INFORMATION allineati a:
// https://github.com/dotnet/runtime/blob/release/8.0/src/libraries/Common/src/Interop/Windows/NtDll/Interop.SYSTEM_PROCESS_INFORMATION.cs

using System.Runtime.InteropServices;

namespace SysSuite.Interop
{
    internal static unsafe class NtDll
    {
        /// <summary>SYSTEM_INFORMATION_CLASS.SystemProcessInformation</summary>
        internal const int SystemProcessInformationClass = 5;

        /// <summary>NTSTATUS STATUS_INFO_LENGTH_MISMATCH</summary>
        internal const uint StatusInfoLengthMismatch = 0xC0000004;

        [DllImport("ntdll.dll")]
        internal static extern int NtQuerySystemInformation(
            int systemInformationClass,
            void* systemInformation,
            uint systemInformationLength,
            uint* returnLength);

        // https://learn.microsoft.com/windows/win32/api/subauth/ns-subauth-unicode_string
        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct UnicodeString
        {
            internal readonly ushort Length;
            internal readonly ushort MaximumLength;
            internal readonly nint Buffer;
        }

        /// <summary>Allineamento binario al buffer kernel (classe 5).</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct SystemProcessInformation
        {
            internal uint NextEntryOffset;
            internal uint NumberOfThreads;
            private fixed byte Reserved1[48];
            internal UnicodeString ImageName;
            internal int BasePriority;
            internal nint UniqueProcessId;
            private readonly nuint Reserved2;
            internal uint HandleCount;
            internal uint SessionId;
            private readonly nuint Reserved3;
            internal nuint PeakVirtualSize;
            internal nuint VirtualSize;
            private readonly uint Reserved4;
            internal nuint PeakWorkingSetSize;
            internal nuint WorkingSetSize;
            private readonly nuint Reserved5;
            internal nuint QuotaPagedPoolUsage;
            private readonly nuint Reserved6;
            internal nuint QuotaNonPagedPoolUsage;
            internal nuint PagefileUsage;
            internal nuint PeakPagefileUsage;
            internal nuint PrivatePageCount;
            private fixed long Reserved7[6];
        }
    }
}
