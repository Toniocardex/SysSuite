// SPDX-License-Identifier: MIT
// Logica di enumerazione ispirata a NtProcessInfoHelper (System.Diagnostics.Process), dotnet/runtime release/8.0.

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SysSuite.Interop
{
    /// <summary>
    /// Enumerazione processi tramite <c>NtQuerySystemInformation(SystemProcessInformation)</c>
    /// e parsing zero-copy con <see cref="MemoryMarshal"/>.
    /// </summary>
    internal static unsafe class NtProcessInfoReader
    {
        private const uint DefaultBufferSize = 1024 * 1024;
        private static uint s_lastGoodBufferSize = DefaultBufferSize;

        internal readonly record struct RawProcessRow(
            int Pid,
            string Name,
            uint ThreadCount,
            uint HandleCount,
            long WorkingSetBytes,
            long UserTime100Ns,
            long KernelTime100Ns,
            long CreateTime100Ns);

        internal static List<RawProcessRow> EnumerateProcesses()
        {
            uint bufferSize = s_lastGoodBufferSize;
            while (true)
            {
                void* buffer = NativeMemory.Alloc(bufferSize);
                try
                {
                    uint actual = 0;
                    int status = NtDll.NtQuerySystemInformation(
                        NtDll.SystemProcessInformationClass,
                        buffer,
                        bufferSize,
                        &actual);

                    if ((uint)status == NtDll.StatusInfoLengthMismatch)
                    {
                        bufferSize = Math.Max(actual + 1024 * 64, bufferSize + 64 * 1024);
                        continue;
                    }

                    if (status < 0)
                        throw new InvalidOperationException("NtQuerySystemInformation fallita: 0x" + status.ToString("X8", CultureInfo.InvariantCulture));

                    if (actual == 0 || actual > bufferSize)
                        return new List<RawProcessRow>();

                    s_lastGoodBufferSize = actual + 1024 * 10;
                    return Parse(new ReadOnlySpan<byte>(buffer, (int)actual));
                }
                finally
                {
                    NativeMemory.Free(buffer);
                }
            }
        }

        private static List<RawProcessRow> Parse(ReadOnlySpan<byte> data)
        {
            var list = new List<RawProcessRow>(256);
            int offset = 0;
            while (offset < data.Length)
            {
                ReadOnlySpan<byte> slice = data.Slice(offset);
                if (slice.Length < Unsafe.SizeOf<NtDll.SystemProcessInformation>())
                    break;

                ref readonly NtDll.SystemProcessInformation pi =
                    ref MemoryMarshal.AsRef<NtDll.SystemProcessInformation>(slice);

                int pid = (int)pi.UniqueProcessId;
                // Reserved1 (48 B): … CreateTime @+24, UserTime @+32, KernelTime @+40 (LARGE_INTEGER, 100 ns)
                ReadOnlySpan<byte> b = slice;
                const int r1 = 8;
                long createT = BinaryPrimitives.ReadInt64LittleEndian(b.Slice(r1 + 24, 8));
                long userT = BinaryPrimitives.ReadInt64LittleEndian(b.Slice(r1 + 32, 8));
                long kernelT = BinaryPrimitives.ReadInt64LittleEndian(b.Slice(r1 + 40, 8));

                ReadOnlySpan<char> nameSpan = GetImageNameSpan(in pi);
                string name = nameSpan.Length > 0
                    ? GetProcessShortName(nameSpan).ToString()
                    : pid == 0 ? "Idle"
                    : pid == 4 ? "System"
                    : pid.ToString(CultureInfo.InvariantCulture);

                long ws = (long)pi.WorkingSetSize;
                list.Add(new RawProcessRow(
                    pid,
                    name,
                    pi.NumberOfThreads,
                    pi.HandleCount,
                    ws,
                    userT,
                    kernelT,
                    createT));

                if (pi.NextEntryOffset == 0)
                    break;
                offset += (int)pi.NextEntryOffset;
            }

            return list;
        }

        private static ReadOnlySpan<char> GetImageNameSpan(in NtDll.SystemProcessInformation pi)
        {
            if (pi.ImageName.Buffer == 0 || pi.ImageName.Length == 0)
                return ReadOnlySpan<char>.Empty;
            int charLen = pi.ImageName.Length / sizeof(char);
            if (charLen <= 0)
                return ReadOnlySpan<char>.Empty;
            return new ReadOnlySpan<char>((void*)pi.ImageName.Buffer, charLen);
        }

        /// <summary>Stesso algoritmo di GetProcessShortName nel BCL (perfdlls).</summary>
        private static ReadOnlySpan<char> GetProcessShortName(ReadOnlySpan<char> name)
        {
            int slash = name.LastIndexOf('\\');
            if (slash >= 0 && slash < name.Length - 1)
                name = name.Slice(slash + 1);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
                name = name[..^4];
            return name;
        }
    }
}
