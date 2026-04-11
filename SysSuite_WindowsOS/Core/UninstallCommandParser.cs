using System.Text.RegularExpressions;

namespace SysSuite.Core
{
    /// <summary>
    /// Separa eseguibile e argomenti da stringhe UninstallString del registro (virgolette, MSIEXEC, parametri).
    /// </summary>
    public static class UninstallCommandParser
    {
        /// <summary>
        /// Espande variabili d'ambiente, quindi ricava file e argomenti per Process.Start / ProcessRunner.
        /// </summary>
        public static (string FileName, string Arguments) SplitExecutableAndArguments(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("UninstallString vuota o non valida.", nameof(raw));

            string s = Environment.ExpandEnvironmentVariables(raw).Trim();

            // Percorso tra doppi apici iniziali: "C:\...\uninst.exe" /S ...
            if (s.Length >= 2 && s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 1)
                {
                    string exe = s[1..end];
                    string args = end + 1 < s.Length ? s[(end + 1)..].Trim() : "";
                    return (exe, args);
                }
            }

            // MSIEXEC / rundll32 / altri: primo segmento che termina con .exe (case-insensitive)
            int afterExe = FindFirstExeTokenEnd(s);
            if (afterExe > 0)
            {
                string exe = s[..afterExe].Trim().Trim('"');
                string args = afterExe < s.Length ? s[afterExe..].Trim() : "";
                return (exe, args);
            }

            // Apici singoli rari (alcuni installer legacy)
            if (s.Length >= 2 && s[0] == '\'')
            {
                int end = s.IndexOf('\'', 1);
                if (end > 1)
                {
                    string exe = s[1..end];
                    string args = end + 1 < s.Length ? s[(end + 1)..].Trim() : "";
                    return (exe, args);
                }
            }

            // Regex: comando senza spazi nel path (token iniziale)
            var m = Regex.Match(s, @"^(?<exe>[^\s""]+)(?<rest>\s+.+)?$");
            if (m.Success)
            {
                string exe = m.Groups["exe"].Value.Trim('"');
                string args = m.Groups["rest"].Success ? m.Groups["rest"].Value.Trim() : "";
                return (exe, args);
            }

            return (s.Trim('"'), "");
        }

        /// <summary>Indice subito dopo la prima occorrenza di .exe che delimita l'eseguibile.</summary>
        private static int FindFirstExeTokenEnd(string s)
        {
            var lower = s.ToLowerInvariant();
            int idx = 0;
            while (idx < lower.Length)
            {
                int p = lower.IndexOf(".exe", idx, StringComparison.Ordinal);
                if (p < 0) return -1;
                int after = p + 4;
                if (after >= s.Length || char.IsWhiteSpace(s[after]))
                    return after;
                idx = after;
            }

            return -1;
        }
    }
}
