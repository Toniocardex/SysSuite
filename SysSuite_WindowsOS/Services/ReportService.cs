using SysSuite.Core;
using System.Text;

namespace SysSuite.Services
{
    /// <summary>Genera report TXT e HTML dello stato del sistema.</summary>
    public class ReportService
    {
        public string GenerateTxt(SystemInfo info)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================");
            sb.AppendLine("  SYSSUITE ONE v1.0 - REPORT DI SISTEMA");
            sb.AppendLine($"  Autore: Antonio Cardelli - Milano, IT");
            sb.AppendLine($"  Data: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine("================================================");
            sb.AppendLine();
            sb.AppendLine("[SISTEMA OPERATIVO]");
            sb.AppendLine($"  Nome    : {info.OSName}");
            sb.AppendLine($"  Versione: {info.OSVersion}");
            sb.AppendLine();
            sb.AppendLine("[PROCESSORE]");
            sb.AppendLine($"  Modello : {info.CPUName}");
            sb.AppendLine($"  Core    : {info.CPUCores} fisici / {info.CPUThreads} logici");
            sb.AppendLine();
            sb.AppendLine("[MEMORIA RAM]");
            sb.AppendLine($"  Totale  : {info.RAMTotalGB} GB");
            sb.AppendLine($"  Libera  : {info.RAMFreeGB} GB  ({info.RAMUsedPct}% usata)");
            sb.AppendLine();
            sb.AppendLine("[DISCO C:]");
            sb.AppendLine($"  Totale  : {info.DiskTotalGB} GB");
            sb.AppendLine($"  Libero  : {info.DiskFreeGB} GB  ({info.DiskUsedPct}% usato)");
            sb.AppendLine();
            sb.AppendLine($"[UPTIME]  {info.Uptime.Days}g {info.Uptime.Hours}h {info.Uptime.Minutes}m");
            sb.AppendLine();
            sb.AppendLine("[PROCESSI - TOP 10 PER RAM]");
            foreach (var p in System.Diagnostics.Process.GetProcesses()
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                .Take(10))
            {
                try { sb.AppendLine($"  {p.ProcessName,-30} {Math.Round(p.WorkingSet64/1_048_576.0,1),8} MB"); }
                catch { }
            }
            return sb.ToString();
        }

        public string GenerateHtml(SystemInfo info)
        {
            return $@"<!DOCTYPE html>
<html lang=""it"">
<head>
  <meta charset=""utf-8"">
  <title>SysSuite One - Report</title>
  <style>
    body {{ background:#0a0c14; color:#dce6ff; font-family:Consolas,monospace; padding:30px; }}
    h1   {{ color:#00c896; }}
    h2   {{ color:#008cff; border-bottom:1px solid #1e2a40; padding-bottom:6px; }}
    table{{ width:100%; border-collapse:collapse; margin-bottom:20px; }}
    td,th{{ padding:8px 12px; border:1px solid #1e2a40; }}
    th   {{ background:#12162b; color:#00c896; text-align:left; }}
  </style>
</head>
<body>
  <h1>SysSuite One - Report di Sistema</h1>
  <p>Generato il: {DateTime.Now:dd/MM/yyyy HH:mm:ss} | Autore: Antonio Cardelli</p>
  <h2>Sistema Operativo</h2>
  <table>
    <tr><th>Campo</th><th>Valore</th></tr>
    <tr><td>Nome</td><td>{info.OSName}</td></tr>
    <tr><td>Versione</td><td>{info.OSVersion}</td></tr>
    <tr><td>Uptime</td><td>{info.Uptime.Days}g {info.Uptime.Hours}h {info.Uptime.Minutes}m</td></tr>
  </table>
  <h2>Hardware</h2>
  <table>
    <tr><th>Campo</th><th>Valore</th></tr>
    <tr><td>CPU</td><td>{info.CPUName}</td></tr>
    <tr><td>Core</td><td>{info.CPUCores} fisici / {info.CPUThreads} logici</td></tr>
    <tr><td>RAM Totale</td><td>{info.RAMTotalGB} GB</td></tr>
    <tr><td>RAM Libera</td><td>{info.RAMFreeGB} GB ({info.RAMUsedPct}% usata)</td></tr>
    <tr><td>Disco C: Totale</td><td>{info.DiskTotalGB} GB</td></tr>
    <tr><td>Disco C: Libero</td><td>{info.DiskFreeGB} GB ({info.DiskUsedPct}% usato)</td></tr>
  </table>
</body>
</html>";
        }

        public string SaveTxt(SystemInfo info)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SysSuite_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, GenerateTxt(info), Encoding.UTF8);
            return path;
        }

        public string SaveHtml(SystemInfo info)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SysSuite_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(path, GenerateHtml(info), Encoding.UTF8);
            return path;
        }
    }
}