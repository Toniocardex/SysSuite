// SPDX-License-Identifier: MIT
// Accesso disco fisico: CreateFile + DeviceIoControl (nessun WMI).

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SysSuite.Interop;

/// <summary>P/Invoke kernel32 per handle disco e <see cref="DeviceIoControl"/> (IOCTL storage).</summary>
internal static partial class NtStorage
{
    internal const uint GenericRead = 0x80000000;

    /// <summary>Alcuni IOCTL storage richiedono anche scrittura sul handle del disco fisico.</summary>
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 1;
    internal const uint FileShareWrite = 2;
    internal const uint OpenExisting = 3;
    internal const uint FileAttributeNormal = 0x80;

    internal const uint IoctlStorageQueryProperty = 0x002D1400;

    /// <summary><c>CTL_CODE(FILE_DEVICE_VOLUME, 0, METHOD_BUFFERED, FILE_ANY_ACCESS)</c></summary>
    internal const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

    /// <summary>Da <c>winioctl.h</c> (ordine ufficiale post-IoCapability=48).</summary>
    internal const int StorageDeviceProtocolSpecificProperty = 50;

    internal const int StorageAdapterTemperatureProperty = 51;

    internal const int StorageDeviceTemperatureProperty = 52;

    internal const uint ProtocolTypeNvme = 3;

    internal const uint NvMeDataTypeLogPage = 2;

    internal static readonly nint InvalidHandleValue = new(-1);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeviceIoControl(
        nint hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        int nInBufferSize,
        nint lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        nint lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    /// <summary>
    /// Risolve il numero <c>PhysicalDriveN</c> del volume di sistema (es. disco di <c>C:</c>) via IOCTL volume (no WMI).
    /// </summary>
    internal static bool TryGetPhysicalDriveNumberForSystemVolume(out int physicalDriveNumber)
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        return TryGetPhysicalDriveNumberForVolumeRoot(root, out physicalDriveNumber);
    }

    internal static bool TryGetPhysicalDriveNumberForVolumeRoot(string volumeRoot, out int physicalDriveNumber)
    {
        physicalDriveNumber = 0;
        try
        {
            string root = volumeRoot.Trim();
            if (root.Length < 2 || root[1] != ':')
                return false;
            char letter = char.ToUpperInvariant(root[0]);
            if (letter is < 'A' or > 'Z')
                return false;

            string volumePath = "\\\\.\\" + letter + ":";
            nint h = CreateFileW(
                volumePath,
                GenericRead,
                FileShareRead | FileShareWrite,
                nint.Zero,
                OpenExisting,
                FileAttributeNormal,
                nint.Zero);
            if (h == InvalidHandleValue)
                return false;
            try
            {
                int outBytes = 256;
                nint pOut = Marshal.AllocHGlobal(outBytes);
                try
                {
                    if (!DeviceIoControl(
                            h,
                            IoctlVolumeGetVolumeDiskExtents,
                            nint.Zero,
                            0,
                            pOut,
                            outBytes,
                            out int returned,
                            nint.Zero))
                        return false;

                    if (returned < Marshal.SizeOf<VolumeDiskExtentsNative>())
                        return false;

                    var ext = Marshal.PtrToStructure<VolumeDiskExtentsNative>(pOut);
                    if (ext.NumberOfDiskExtents < 1)
                        return false;
                    physicalDriveNumber = (int)ext.DiskNumber;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(pOut);
                }
            }
            finally
            {
                _ = CloseHandle(h);
            }
        }
        catch
        {
            return false;
        }
    }

    internal static nint OpenPhysicalDriveForIoctl(int physicalDriveNumber)
    {
        string path = "\\\\.\\PhysicalDrive" + physicalDriveNumber;
        return CreateFileW(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            FileAttributeNormal,
            nint.Zero);
    }

    /// <summary>
    /// <see cref="STORAGE_PROPERTY_QUERY"/> + <see cref="STORAGE_PROTOCOL_SPECIFIC_DATA"/> impacchettati
    /// (equivalente a <c>AdditionalParameters</c> che inizia il blocco protocollo).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StorageNvmeHealthQuery
    {
        internal int PropertyId;
        internal int QueryType;
        internal uint ProtocolType;
        internal uint DataType;
        internal uint ProtocolDataRequestValue;
        internal uint ProtocolDataRequestSubValue;
        internal uint ProtocolDataOffset;
        internal uint ProtocolDataLength;
        internal uint FixedProtocolReturnData;
        internal uint Reserved0;
        internal uint Reserved1;
        internal uint Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StoragePropertyQueryHeader
    {
        internal int PropertyId;
        internal int QueryType;
        internal byte AdditionalParameter;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StorageTemperatureDataDescriptor
    {
        internal uint Version;
        internal uint Size;
        internal short CriticalTemperature;
        internal short WarningTemperature;
        internal ushort InfoCount;
        internal ushort Reserved0;
        internal uint Reserved1A;
        internal uint Reserved1B;
    }

    /// <summary>Primo extent da <see cref="VolumeDiskExtentsNative"/> (layout MSVC / MSDN).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VolumeDiskExtentsNative
    {
        internal uint NumberOfDiskExtents;
        internal long StartingOffset;
        internal long ExtentLength;
        internal uint DiskNumber;
        internal uint PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StorageTemperatureInfo
    {
        internal ushort Index;
        internal short Temperature;
        internal short OverThreshold;
        internal short UnderThreshold;
        internal byte OverThresholdChangable;
        internal byte UnderThresholdChangable;
        internal byte EventGenerated;
        internal byte Reserved0;
        internal uint Reserved1;
    }

}
