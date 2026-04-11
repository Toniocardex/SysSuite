using SysSuite.Core;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>Pulizia file temporanei, cache, cestino, browser.</summary>
    public class CleanupService
    {
        public event Action<string,string>? Log;

        public void CleanTemp()
        {
            DeleteFolder(Path.GetTempPath(), "Temp utente");
        }
        public void CleanWinTemp()
        {
            DeleteFolder(@"C:\Windows\Temp", "Windows Temp");
        }
        public void CleanPrefetch()
        {
            DeleteFolder(@"C:\Windows\Prefetch", "Prefetch");
        }

        public async Task CleanRecycleBinAsync(CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAsync("cmd.exe", "/c rd /s /q %SystemDrive%\\$Recycle.bin", cancellationToken).ConfigureAwait(false);
            Emit("Cestino svuotato", "ok");
        }

        public void CleanThumbnails()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer");
            foreach (var f in Directory.GetFiles(path, "thumbcache_*.db", SearchOption.TopDirectoryOnly))
                TryDelete(f);
            Emit("Cache miniature rimossa", "ok");
        }

        public async Task CleanWindowsUpdateAsync(CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAsync("net.exe", "stop wuauserv", cancellationToken).ConfigureAwait(false);
            DeleteFolder(@"C:\Windows\SoftwareDistribution\Download", "Cache Windows Update");
            await ProcessRunner.RunAsync("net.exe", "start wuauserv", cancellationToken).ConfigureAwait(false);
        }

        // ── Duplicati ────────────────────────────────────────
        public List<DuplicateGroup> FindDuplicates(string root, IProgress<string>? progress = null)
        {
            var groups = new List<DuplicateGroup>();
            try
            {
                // Fase 1: raggruppamento rapido per dimensione + nome
                var candidates = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Select(f => { try { return new FileInfo(f); } catch { return null; } })
                    .Where(f => f != null && f.Length > 1024)
                    .GroupBy(f => $"{f!.Length}|{f.Name}")
                    .Where(g => g.Count() > 1);

                int i = 0;
                foreach (var grp in candidates)
                {
                    i++;
                    progress?.Report($"Analisi gruppo {i}: {grp.Key}");

                    // Fase 2: calcola hash di OGNI file e sotto-raggruppa
                    var filesWithHash = grp.Select(f => new
                    {
                        Info = f!,
                        Hash = ComputeHash(f!.FullName)
                    }).Where(x => !string.IsNullOrEmpty(x.Hash)).ToList();

                    // Fase 3: solo i file con hash identico sono veri duplicati
                    var realDuplicates = filesWithHash
                        .GroupBy(x => x.Hash)
                        .Where(hg => hg.Count() > 1);

                    foreach (var hashGroup in realDuplicates)
                    {
                        var first = hashGroup.First();
                        groups.Add(new DuplicateGroup
                        {
                            FileName  = first.Info.Name,
                            SizeBytes = first.Info.Length,
                            Hash      = first.Hash,
                            Files     = hashGroup.Select(x => new DiskEntry
                            {
                                Path      = x.Info.DirectoryName ?? "",
                                Name      = x.Info.Name,
                                SizeBytes = x.Info.Length,
                                LastWrite = x.Info.LastWriteTime
                            }).ToList()
                        });
                    }
                }
            }
            catch (Exception ex) { Emit($"Duplicati: {ex.Message}", "err"); }
            return groups.OrderByDescending(g => g.WastedBytes).ToList();
        }

        public Task<List<DuplicateGroup>> FindDuplicatesAsync(string root, IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => FindDuplicates(root, progress), cancellationToken);

        // ── Utils ────────────────────────────────────────────
        private void DeleteFolder(string path, string label)
        {
            if (!Directory.Exists(path)) return;
            int deleted = 0;
            foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                if (TryDelete(f)) deleted++;
            foreach (var d in Directory.GetDirectories(path))
                try { Directory.Delete(d, true); } catch { }
            Emit($"{label} rimosso ({deleted} file)", "ok");
        }

        private static bool TryDelete(string path)
        {
            try { File.Delete(path); return true; } catch { return false; }
        }


        private static string ComputeHash(string path)
        {
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var fs  = File.OpenRead(path);
                return BitConverter.ToString(sha.ComputeHash(fs));
            }
            catch { return ""; }
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }
}
