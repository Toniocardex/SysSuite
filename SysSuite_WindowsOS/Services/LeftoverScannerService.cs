using System.Collections.Concurrent;
using System.IO;
using Microsoft.Win32;
using Serilog;

namespace SysSuite.Services
{
    /// <summary>
    /// Motore euristico post-disinstallazione: solo lettura in scansione; eliminazione solo su richiesta esplicita.
    /// </summary>
    public class LeftoverScannerService
    {
        public const string RegistryResultPrefix = "::REGISTRY::";

        /// <summary>
        /// Cerca cartelle e chiavi di registro che sembrano collegate all'app (solo lettura).
        /// </summary>
        public Task<List<string>> ScanLeftoversAsync(string appName, string publisher,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => ScanLeftoversCore(appName, publisher, cancellationToken), cancellationToken);

        /// <summary>
        /// Elimina i percorsi restituiti da <see cref="ScanLeftoversAsync"/> (file, directory o chiavi REG: prefisso <see cref="RegistryResultPrefix"/>).
        /// </summary>
        public Task DeleteLeftoversAsync(IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => DeleteLeftoversCore(paths, cancellationToken), cancellationToken);

        private static List<string> ScanLeftoversCore(string appName, string publisher,
            CancellationToken cancellationToken)
        {
            var hits = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            void AddHit(string path, string kind)
            {
                if (hits.TryAdd(path, 0))
                    Log.Information("Residuo rilevato ({Kind}): {Path}", kind, path);
            }

            foreach (var root in GetFileSystemRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;
                ScanDirectoryHeuristic(root, maxDepth: 3, appName, publisher, AddHit, cancellationToken);
            }

            // HKCU\Software e HKLM\Software (incluso WOW6432Node come sottochiave di Software)
            ScanRegistrySoftware(Registry.CurrentUser, @"Software", appName, publisher, maxDepth: 3, hits,
                cancellationToken);
            ScanRegistrySoftware(Registry.LocalMachine, @"Software", appName, publisher, maxDepth: 3, hits,
                cancellationToken);

            return hits.Keys.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Solo %AppData%, %LocalAppData%, %ProgramData% (CommonApplicationData).</summary>
        private static IEnumerable<string> GetFileSystemRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        }

        /// <summary>
        /// DFS limitata: nome cartella o percorso completo contiene app o publisher (publisher ignorato se troppo corto).
        /// </summary>
        private static void ScanDirectoryHeuristic(string root, int maxDepth, string appName, string publisher,
            Action<string, string> addHit, CancellationToken cancellationToken)
        {
            void Visit(string dir, int depth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (depth > maxDepth || !Directory.Exists(dir))
                    return;

                try
                {
                    if (HeuristicMatch(Path.GetFileName(dir), appName, publisher)
                        || HeuristicMatch(dir, appName, publisher))
                        addHit(dir, "cartella");

                    if (depth == maxDepth)
                        return;

                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            Visit(sub, depth + 1);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Log.Debug(ex, "Scansione residui: accesso negato a {Dir}", sub);
                        }
                        catch (IOException ex)
                        {
                            Log.Debug(ex, "Scansione residui: I/O su {Dir}", sub);
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Scansione residui: errore su {Dir}", sub);
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Debug(ex, "Scansione residui: accesso negato a {Dir}", dir);
                }
                catch (IOException ex)
                {
                    Log.Debug(ex, "Scansione residui: I/O su {Dir}", dir);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Scansione residui: errore su {Dir}", dir);
                }
            }

            Visit(root, 0);
        }

        private static void ScanRegistrySoftware(RegistryKey hiveRoot, string relativePath, string appName,
            string publisher, int maxDepth, ConcurrentDictionary<string, byte> hits, CancellationToken cancellationToken)
        {
            void Recurse(RegistryKey key, int depth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] names;
                try
                {
                    names = key.GetSubKeyNames();
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Debug(ex, "Scansione registro: accesso negato a {Key}", key.Name);
                    return;
                }
                catch (IOException ex)
                {
                    Log.Debug(ex, "Scansione registro: I/O su {Key}", key.Name);
                    return;
                }

                foreach (var name in names)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var sub = key.OpenSubKey(name, writable: false);
                        if (sub == null) continue;

                        if (HeuristicMatch(name, appName, publisher))
                        {
                            string marked = RegistryResultPrefix + sub.Name;
                            if (hits.TryAdd(marked, 0))
                                Log.Information("Residuo rilevato (registro): {Key}", sub.Name);
                        }

                        if (depth < maxDepth)
                            Recurse(sub, depth + 1);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log.Debug(ex, "Scansione registro: accesso negato a sottochiave di {Key}", key.Name);
                    }
                    catch (IOException ex)
                    {
                        Log.Debug(ex, "Scansione registro: I/O su sottochiave di {Key}", key.Name);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Scansione registro: errore su sottochiave di {Key}", key.Name);
                    }
                }
            }

            try
            {
                using var software = hiveRoot.OpenSubKey(relativePath, writable: false);
                if (software == null) return;
                Recurse(software, 0);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Debug(ex, "Scansione registro: impossibile aprire {Hive}\\{Path}", hiveRoot.Name, relativePath);
            }
            catch (IOException ex)
            {
                Log.Debug(ex, "Scansione registro: I/O su {Hive}\\{Path}", hiveRoot.Name, relativePath);
            }
        }

        private static bool HeuristicMatch(string candidate, string appName, string publisher)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (!string.IsNullOrWhiteSpace(appName)
                && candidate.Contains(appName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
            string pub = publisher?.Trim() ?? "";
            if (pub.Length >= 3
                && candidate.Contains(pub, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static void DeleteLeftoversCore(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        {
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    if (path.StartsWith(RegistryResultPrefix, StringComparison.OrdinalIgnoreCase))
                        TryDeleteRegistryKey(path);
                    else
                        TryDeleteFileSystemPath(path);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Residuo: errore imprevisto durante l'eliminazione di un elemento: {Path}", path);
                }
            }
        }

        private static void TryDeleteRegistryKey(string markedPath)
        {
            string full = markedPath[RegistryResultPrefix.Length..].Trim();
            try
            {
                if (full.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
                {
                    string sub = full["HKEY_CURRENT_USER".Length..].TrimStart('\\');
                    Registry.CurrentUser.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
                    Log.Information("Residuo registro eliminato: {Key}", full);
                    return;
                }

                if (full.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
                {
                    string sub = full["HKEY_LOCAL_MACHINE".Length..].TrimStart('\\');
                    Registry.LocalMachine.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
                    Log.Information("Residuo registro eliminato: {Key}", full);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warning(ex, "Impossibile eliminare chiave registro (permessi): {Key}", full);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Impossibile eliminare chiave registro (I/O): {Key}", full);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Impossibile eliminare chiave registro: {Key}", full);
            }
        }

        private static void TryDeleteFileSystemPath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    Log.Information("Residuo cartella eliminata: {Path}", path);
                    return;
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.Information("Residuo file eliminato: {Path}", path);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warning(ex, "Impossibile eliminare percorso (accesso negato): {Path}", path);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Impossibile eliminare percorso (file in uso o I/O): {Path}", path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Impossibile eliminare percorso: {Path}", path);
            }
        }
    }
}
