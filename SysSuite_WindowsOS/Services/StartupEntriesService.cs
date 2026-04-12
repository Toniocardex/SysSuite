using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SysSuite.Interop;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>
    /// Voci di avvio: enumerazione nativa (advapi32) con buffer UTF-16 riusati;
    /// <see cref="StartupEntry"/> solo dopo validazione comando. Fallback <see cref="Registry"/> in caso di errore.
    /// Scrittura (disabilitazione) tramite <see cref="Registry"/>; paracadute WMI tramite <see cref="SystemRestoreService"/>.
    /// </summary>
    public sealed class StartupEntriesService
    {
        private readonly SystemRestoreService _systemRestore;

        public event Action<string, string>? Log;

        public StartupEntriesService(SystemRestoreService systemRestore) =>
            _systemRestore = systemRestore;

        private const string SubRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SubRunOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

        public List<StartupEntry> GetStartupEntries()
        {
            try
            {
                var list = new List<StartupEntry>(64);
                AppendRunHiveNative(NtRegistry.HkeyCurrentUser, SubRun, @"HKCU\" + SubRun, "HKCU Run", list);
                AppendRunHiveNative(NtRegistry.HkeyLocalMachine, SubRun, @"HKLM\" + SubRun, "HKLM Run", list);
                AppendRunHiveNative(NtRegistry.HkeyCurrentUser, SubRunOnce, @"HKCU\" + SubRunOnce, "HKCU RunOnce", list);
                AppendRunHiveNative(NtRegistry.HkeyLocalMachine, SubRunOnce, @"HKLM\" + SubRunOnce, "HKLM RunOnce", list);
                AppendStartupFolder(list);
                return list;
            }
            catch (Exception ex)
            {
                Log?.Invoke("Enumerazione avvio nativa fallita: " + ex.Message, "warn");
                global::Serilog.Log.Warning(ex, "[Startup] fallback su Microsoft.Win32.Registry");
                return CollectManagedFallback();
            }
        }

        private void AppendStartupFolder(List<StartupEntry> result)
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                foreach (string f in Directory.GetFiles(startupFolder))
                {
                    result.Add(new StartupEntry
                    {
                        Name = Path.GetFileName(f),
                        Command = f,
                        Source = "Cartella Avvio",
                        RegPath = startupFolder
                    });
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke("Cartella Avvio: " + ex.Message, "warn");
            }
        }

        private unsafe void AppendRunHiveNative(
            nint hive,
            string subKeyRelative,
            string displayRegPath,
            string sourceLabel,
            List<StartupEntry> sink)
        {
            nint hKey = 0;
            int openSt;
            fixed (char* pSub = subKeyRelative)
            {
                openSt = NtRegistry.RegOpenKeyExW(hive, pSub, 0, NtRegistry.KeyRead, &hKey);
            }

            if (openSt != NtRegistry.ErrorSuccess)
            {
                if (openSt == NtRegistry.ErrorAccessDenied && hive == NtRegistry.HkeyLocalMachine)
                {
                    string msg = $"HKLM non leggibile (0x{openSt:X}): {displayRegPath} — privilegi amministratore assenti?";
                    Log?.Invoke(msg, "warn");
                    global::Serilog.Log.Warning("[Startup] {Message}", msg);
                }
                else if (openSt != NtRegistry.ErrorFileNotFound)
                    Log?.Invoke($"RegOpenKeyEx {displayRegPath}: codice {openSt}", "warn");
                return;
            }

            try
            {
                ProbeSubKeysWithRegEnumKeyEx(hKey, displayRegPath);

                int nameCapChars = 256;
                int dataCapBytes = 4096;
                int expandCapChars = 4096;
                char* nameBuf = (char*)NativeMemory.Alloc((nuint)(nameCapChars * sizeof(char)));
                byte* dataBuf = (byte*)NativeMemory.Alloc((nuint)dataCapBytes);
                char* expandBuf = (char*)NativeMemory.Alloc((nuint)(expandCapChars * sizeof(char)));
                try
                {
                    uint index = 0;
                    while (true)
                    {
                        uint nameChars = (uint)nameCapChars;
                        uint dataBytes = (uint)dataCapBytes;
                        int ev = NtRegistry.RegEnumValueW(
                            hKey,
                            index,
                            nameBuf,
                            ref nameChars,
                            nint.Zero,
                            out uint type,
                            dataBuf,
                            ref dataBytes);

                        if (ev == NtRegistry.ErrorNoMoreItems)
                            break;

                        if (ev == NtRegistry.ErrorMoreData)
                        {
                            int needNameChars = (int)Math.Max(nameCapChars, nameChars);
                            int needDataBytes = (int)Math.Max(dataCapBytes, (int)dataBytes);
                            NativeMemory.Free(nameBuf);
                            NativeMemory.Free(dataBuf);
                            nameCapChars = Math.Min(Math.Max(needNameChars, 256), 16_384);
                            dataCapBytes = Math.Min(Math.Max(needDataBytes, 4096), 1_048_576);
                            nameBuf = (char*)NativeMemory.Alloc((nuint)(nameCapChars * sizeof(char)));
                            dataBuf = (byte*)NativeMemory.Alloc((nuint)dataCapBytes);
                            continue;
                        }

                        if (ev != NtRegistry.ErrorSuccess)
                        {
                            Log?.Invoke($"RegEnumValue {displayRegPath} idx={index}: {ev}", "warn");
                            index++;
                            continue;
                        }

                        if (nameChars == 0)
                        {
                            index++;
                            continue;
                        }

                        ReadOnlySpan<char> nameSpan = new(nameBuf, (int)nameChars);
                        if (!TryBuildCommandSpan(
                                type,
                                dataBuf,
                                dataBytes,
                                dataCapBytes,
                                ref expandBuf,
                                ref expandCapChars,
                                out ReadOnlySpan<char> commandSpan))
                        {
                            index++;
                            continue;
                        }

                        if (!IsValidStartupCandidate(nameSpan, commandSpan))
                        {
                            index++;
                            continue;
                        }

                        ReadOnlySpan<char> cmdTrim = commandSpan.Trim();
                        sink.Add(new StartupEntry
                        {
                            Name = new string(nameSpan),
                            Command = new string(cmdTrim),
                            Source = sourceLabel,
                            RegPath = displayRegPath
                        });
                        index++;
                    }
                }
                finally
                {
                    NativeMemory.Free(nameBuf);
                    NativeMemory.Free(dataBuf);
                    NativeMemory.Free(expandBuf);
                }
            }
            finally
            {
                _ = NtRegistry.RegCloseKey(hKey);
            }
        }

        /// <summary>
        /// RegEnumKeyEx con <see cref="Span{T}"/> su stack: nessuna stringa allocata; avviso se esiste almeno una sotto-chiave.
        /// </summary>
        private unsafe void ProbeSubKeysWithRegEnumKeyEx(nint hKey, string displayRegPath)
        {
            const int bufChars = 256;
            uint cch = bufChars;
            Span<char> stackBuf = stackalloc char[bufChars];
            fixed (char* pName = stackBuf)
            {
                int sk = NtRegistry.RegEnumKeyExW(
                    hKey,
                    0,
                    pName,
                    ref cch,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero);
                if (sk == NtRegistry.ErrorSuccess && cch > 0)
                    Log?.Invoke($"Startup: sotto-chiave presente in {displayRegPath} (layout non standard Run).", "warn");
            }
        }

        /// <summary>
        /// Costruisce lo span del comando senza allocare <see cref="string"/>; REG_EXPAND_SZ usa buffer expand riusabile.
        /// </summary>
        private static unsafe bool TryBuildCommandSpan(
            uint type,
            byte* dataBuf,
            uint dataBytes,
            int dataCapBytes,
            ref char* expandBuf,
            ref int expandCapChars,
            out ReadOnlySpan<char> commandSpan)
        {
            commandSpan = ReadOnlySpan<char>.Empty;
            if (dataBytes < sizeof(char))
                return false;

            int wcharLen = (int)(dataBytes / sizeof(char));
            char* wp = (char*)dataBuf;
            while (wcharLen > 0 && wp[wcharLen - 1] == '\0')
                wcharLen--;

            if (wcharLen <= 0)
                return false;

            ReadOnlySpan<char> raw = new(wp, wcharLen);

            if (type == NtRegistry.RegSz)
            {
                commandSpan = raw;
                return true;
            }

            if (type == NtRegistry.RegMultiSz)
            {
                int end = 0;
                while (end < raw.Length && raw[end] != '\0')
                    end++;
                commandSpan = raw[..end];
                return !commandSpan.IsEmpty;
            }

            if (type == NtRegistry.RegExpandSz)
            {
                if ((wcharLen + 1) * sizeof(char) > dataCapBytes)
                    return false;
                wp[wcharLen] = '\0';

                while (true)
                {
                    uint need = NtRegistry.ExpandEnvironmentStringsW(wp, expandBuf, (uint)expandCapChars);
                    if (need == 0)
                        return false;
                    if (need > (uint)expandCapChars)
                    {
                        NativeMemory.Free(expandBuf);
                        expandCapChars = (int)Math.Min(need + 256, 32_768);
                        expandBuf = (char*)NativeMemory.Alloc((nuint)(expandCapChars * sizeof(char)));
                        continue;
                    }

                    int outChars = (int)need;
                    if (outChars > 0 && expandBuf[outChars - 1] == '\0')
                        outChars--;
                    commandSpan = new ReadOnlySpan<char>(expandBuf, outChars);
                    return !commandSpan.IsEmpty;
                }
            }

            return false;
        }

        private static bool IsValidStartupCandidate(ReadOnlySpan<char> name, ReadOnlySpan<char> command)
        {
            if (name.IsEmpty || name.Trim().IsEmpty)
                return false;
            if (command.IsEmpty || command.Trim().IsEmpty)
                return false;
            return true;
        }

        private List<StartupEntry> CollectManagedFallback()
        {
            var result = new List<StartupEntry>();
            var regPaths = new[]
            {
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"),
                (@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"),
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce"),
                (@"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce"),
            };
            foreach (var (path, label) in regPaths)
            {
                try
                {
                    RegistryKey root = path.StartsWith("HKCU", StringComparison.Ordinal)
                        ? Registry.CurrentUser
                        : Registry.LocalMachine;
                    string sub = path[(path.IndexOf('\\', StringComparison.Ordinal) + 1)..];
                    using RegistryKey? key = root.OpenSubKey(sub);
                    if (key == null)
                        continue;
                    foreach (string name in key.GetValueNames())
                    {
                        result.Add(new StartupEntry
                        {
                            Name = name,
                            Command = key.GetValue(name)?.ToString() ?? "",
                            Source = label,
                            RegPath = path
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Fallback {path}: {ex.Message}", "warn");
                }
            }

            AppendStartupFolder(result);
            return result;
        }

        /// <summary>
        /// Disabilita una voce di avvio (registro o file in cartella Startup). Prima di scrivere, tenta un punto di ripristino WMI se il ripristino è attivo.
        /// </summary>
        public async Task<bool> DisableStartupAsync(StartupEntry entry, CancellationToken cancellationToken = default)
        {
            try
            {
                (bool restoreOn, _) = await _systemRestore
                    .IsSystemRestoreEnabledAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (restoreOn)
                {
                    string rpDesc = "SysSuite Checkpoint: Disabilitazione Voce Startup - " + SanitizeForRestoreDescription(entry.Name);
                    (bool rpOk, uint rv, string? err) = await _systemRestore
                        .CreateRestorePointAsync(rpDesc, cancellationToken)
                        .ConfigureAwait(false);
                    if (!rpOk)
                    {
                        Log?.Invoke(
                            $"Punto di ripristino non creato (ReturnValue={rv}): {err ?? "sconosciuto"}. Procedo comunque con la disabilitazione.",
                            "warn");
                    }
                }

                if (entry.Source.StartsWith("HKCU", StringComparison.Ordinal)
                    || entry.Source.StartsWith("HKLM", StringComparison.Ordinal))
                {
                    RegistryKey root = entry.Source.StartsWith("HKCU", StringComparison.Ordinal)
                        ? Registry.CurrentUser
                        : Registry.LocalMachine;
                    string sub = entry.RegPath[(entry.RegPath.IndexOf('\\') + 1)..];
                    using RegistryKey? key = root.OpenSubKey(sub, true);
                    key?.DeleteValue(entry.Name, false);
                }
                else
                {
                    string disabled = Path.Combine(entry.RegPath, "_disabled_" + entry.Name);
                    File.Move(entry.Command, disabled);
                }

                Log?.Invoke($"Disabilitato: {entry.Name}", "ok");
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Errore: {ex.Message}", "err");
                return false;
            }
        }

        private static string SanitizeForRestoreDescription(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "(senza nome)";
            ReadOnlySpan<char> s = name.Trim().AsSpan();
            if (s.Length > 120)
                s = s[..120];
            Span<char> buf = stackalloc char[s.Length];
            s.CopyTo(buf);
            for (int i = 0; i < buf.Length; i++)
            {
                if (char.IsControl(buf[i]))
                    buf[i] = ' ';
            }

            return new string(buf).Trim();
        }
    }
}
