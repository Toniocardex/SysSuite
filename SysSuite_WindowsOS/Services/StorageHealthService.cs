using System.Runtime.InteropServices;
using SysSuite.Interop;

namespace SysSuite.Services;

/// <summary>
/// Temperatura e log NVMe Health (no WMI): <see cref="NtStorage.DeviceIoControl"/>.
/// </summary>
public sealed class StorageHealthService
{
    private const int NvmeHealthLogId = 0x02;

    /// <summary>
    /// Temperatura °C + usura NVM + TBW sul <c>PhysicalDriveN</c> del volume di sistema (es. disco di <c>C:</c>).
    /// </summary>
    public (int? TempC, byte? PercentageUsed, double? HostWriteTb) TryReadPrimaryDriveMetrics()
    {
        int physicalDriveNumber = 0;
        if (NtStorage.TryGetPhysicalDriveNumberForSystemVolume(out int resolved))
            physicalDriveNumber = resolved;

        nint h = NtStorage.OpenPhysicalDriveForIoctl(physicalDriveNumber);
        if (h == NtStorage.InvalidHandleValue)
            return (null, null, null);
        try
        {
            int? temp = TryReadTemperatureCelsius(h);
            byte[]? log = TryGetNvmeHealthLogBytes(h);
            if (log is not { Length: >= 64 })
                return (temp, null, null);

            byte pct = log[5];
            double? tbw = DecodeNvmeDataUnitsWritten(log.AsSpan(48, 16));
            if (!temp.HasValue)
                temp = TryCompositeTempCFromNvmeHealthLog(log);
            return (temp, pct, tbw);
        }
        finally
        {
            _ = NtStorage.CloseHandle(h);
        }
    }

    private static int? TryReadTemperatureCelsius(nint h)
    {
        int? t = TryStorageTemperatureProperty(h, NtStorage.StorageDeviceTemperatureProperty);
        return t ?? TryStorageTemperatureProperty(h, NtStorage.StorageAdapterTemperatureProperty);
    }

    /// <summary>
    /// Input come <c>STORAGE_PROPERTY_QUERY</c> MSVC (~12 byte con padding).
    /// </summary>
    private static int? TryStorageTemperatureProperty(nint h, int propertyId)
    {
        const int queryBytes = 12;
        nint pIn = Marshal.AllocHGlobal(queryBytes);
        const int outSize = 4096;
        nint pOut = Marshal.AllocHGlobal(outSize);
        try
        {
            for (int i = 0; i < queryBytes; i++)
                Marshal.WriteByte(pIn, i, 0);
            Marshal.WriteInt32(pIn, propertyId);
            Marshal.WriteInt32(pIn + 4, 0);

            if (!NtStorage.DeviceIoControl(
                    h,
                    NtStorage.IoctlStorageQueryProperty,
                    pIn,
                    queryBytes,
                    pOut,
                    outSize,
                    out int returned,
                    nint.Zero))
                return null;

            int hdr = Marshal.SizeOf<NtStorage.StorageTemperatureDataDescriptor>();
            if (returned < hdr + Marshal.SizeOf<NtStorage.StorageTemperatureInfo>())
                return null;

            var desc = Marshal.PtrToStructure<NtStorage.StorageTemperatureDataDescriptor>(pOut);
            if (desc.InfoCount == 0)
                return null;

            var info = Marshal.PtrToStructure<NtStorage.StorageTemperatureInfo>(pOut + hdr);
            return info.Temperature;
        }
        finally
        {
            Marshal.FreeHGlobal(pIn);
            Marshal.FreeHGlobal(pOut);
        }
    }

    private static int? TryCompositeTempCFromNvmeHealthLog(byte[] log)
    {
        if (log.Length < 3)
            return null;
        int kelvin = log[1] | (log[2] << 8);
        if (kelvin is < 150 or > 500)
            return null;
        return kelvin - 273;
    }

    private static byte[]? TryGetNvmeHealthLogBytes(nint h)
    {
        byte[]? r = TryGetNvmeHealthLogBytesCore(h, unchecked((uint)-1));
        return r ?? TryGetNvmeHealthLogBytesCore(h, 1);
    }

    private static byte[]? TryGetNvmeHealthLogBytesCore(nint h, uint namespaceId)
    {
        const int protocolDescriptorBytes = 48;
        const int logBytes = 512;
        const int outSize = 4096;
        nint pOut = Marshal.AllocHGlobal(outSize);
        try
        {
            var q = new NtStorage.StorageNvmeHealthQuery
            {
                PropertyId = NtStorage.StorageDeviceProtocolSpecificProperty,
                QueryType = 0,
                ProtocolType = NtStorage.ProtocolTypeNvme,
                DataType = NtStorage.NvMeDataTypeLogPage,
                ProtocolDataRequestValue = ((logBytes / 4 - 1) << 16) | NvmeHealthLogId,
                ProtocolDataRequestSubValue = namespaceId,
                ProtocolDataOffset = protocolDescriptorBytes,
                ProtocolDataLength = logBytes,
                FixedProtocolReturnData = 0,
                Reserved0 = 0,
                Reserved1 = 0,
                Reserved2 = 0
            };

            int inSize = Marshal.SizeOf<NtStorage.StorageNvmeHealthQuery>();
            nint pIn = Marshal.AllocHGlobal(inSize);
            try
            {
                Marshal.StructureToPtr(q, pIn, false);
                if (!NtStorage.DeviceIoControl(
                        h,
                        NtStorage.IoctlStorageQueryProperty,
                        pIn,
                        inSize,
                        pOut,
                        outSize,
                        out int returned,
                        nint.Zero) || returned < protocolDescriptorBytes + logBytes)
                    return null;

                uint totalDesc = unchecked((uint)Marshal.ReadInt32(pOut + 4));
                int logOffset = protocolDescriptorBytes;
                if (totalDesc <= (uint)returned
                    && totalDesc >= protocolDescriptorBytes + (uint)logBytes)
                    logOffset = (int)totalDesc - logBytes;

                if (returned < logOffset + logBytes)
                    return null;

                var copy = new byte[logBytes];
                Marshal.Copy(pOut + logOffset, copy, 0, logBytes);
                return copy;
            }
            finally
            {
                Marshal.FreeHGlobal(pIn);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pOut);
        }
    }

    /// <summary>128-bit LE, unità = 1000 × 512 byte → TB host.</summary>
    private static double? DecodeNvmeDataUnitsWritten(ReadOnlySpan<byte> sixteen)
    {
        ulong lo = BitConverter.ToUInt64(sixteen);
        ulong hi = BitConverter.ToUInt64(sixteen.Slice(8));
        if (lo == 0 && hi == 0)
            return null;

        const double twoPow64 = 18446744073709551616d;
        double units = hi == 0 ? lo : hi * twoPow64 + lo;
        const double bytesPerUnit = 1000d * 512d;
        return units * bytesPerUnit / 1_000_000_000_000d;
    }
}
