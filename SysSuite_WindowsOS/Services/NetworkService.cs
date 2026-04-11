using SysSuite.Core;
using System.Net;
using System.Net.NetworkInformation;
using SysSuite.Models;

namespace SysSuite.Services
{
    /// <summary>DNS, TCP/IP, scanner porte, ping, speed test.</summary>
    public class NetworkService
    {
        public event Action<string,string>? Log;

        // ── Ottimizzazione ────────────────────────────────────
        public void FlushDNS()
        {
            ProcessRunner.Run("ipconfig.exe", "/flushdns");
            Emit("Cache DNS svuotata", "ok");
        }

        public void ResetTCPIP()
        {
            ProcessRunner.Run("netsh.exe", "int ip reset");
            Emit("Stack TCP/IP resettato", "ok");
        }

        public void ResetWinsock()
        {
            ProcessRunner.Run("netsh.exe", "winsock reset");
            Emit("Winsock resettato", "ok");
        }

        public void SetDNS(string primary, string secondary, string label)
        {
            bool isDHCP = string.IsNullOrEmpty(primary) || label == "DHCP";

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                if (isDHCP)
                {
                    // Ripristina DNS automatico via DHCP
                    ProcessRunner.Run("netsh.exe", $"interface ip set dns \"{nic.Name}\" dhcp");
                }
                else
                {
                    ProcessRunner.Run("netsh.exe", $"interface ip set dns \"{nic.Name}\" static {primary}");
                    if (!string.IsNullOrEmpty(secondary))
                        ProcessRunner.Run("netsh.exe", $"interface ip add dns \"{nic.Name}\" {secondary} index=2");
                }
            }
            Emit(isDHCP ? "DNS ripristinato automatico (DHCP)" : $"DNS {label} impostato ({primary})", "ok");
        }

        public void DisableNagle()
        {
            const string root = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
            using var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
            if (baseKey == null) return;
            foreach (var sub in baseKey.GetSubKeyNames())
            {
                using var k = baseKey.OpenSubKey(sub, true);
                k?.SetValue("TcpAckFrequency", 1);
                k?.SetValue("TCPNoDelay", 1);
            }
            Emit("Algoritmo Nagle disabilitato (latenza ridotta)", "ok");
        }

        public void RestoreNagle()
        {
            const string root = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
            using var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
            if (baseKey == null) return;
            foreach (var sub in baseKey.GetSubKeyNames())
            {
                using var k = baseKey.OpenSubKey(sub, true);
                k?.DeleteValue("TcpAckFrequency", false);
                k?.DeleteValue("TCPNoDelay", false);
            }
            Emit("Parametri TCP ripristinati", "ok");
        }

        // ── Scanner ──────────────────────────────────────────
        public async Task<List<PortEntry>> ScanPortsAsync(IProgress<int>? progress = null)
        {
            var results = new List<PortEntry>();
            var commonPorts = new[] { 21,22,23,25,53,80,110,135,139,143,443,445,
                                       3306,3389,5900,8080,8443,27017 };
            int done = 0;
            foreach (int port in commonPorts)
            {
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    var task = client.ConnectAsync("127.0.0.1", port);
                    if (await Task.WhenAny(task, Task.Delay(200)) == task && client.Connected)
                    {
                        results.Add(new PortEntry
                        {
                            Port  = port,
                            State = "LISTEN",
                            LocalAddr = "127.0.0.1",
                            IsSuspicious = port is 23 or 135 or 139 or 445
                        });
                    }
                }
                catch { }
                done++;
                progress?.Report(done * 100 / commonPorts.Length);
            }
            return results;
        }

        public async Task<double> PingAsync(string host)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 2000);
                return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch { return -1; }
        }

        // ── Speed Test (download) ────────────────────────────
        /// <summary>
        /// Misura la velocità di download scaricando ~10 MB da Cloudflare.
        /// Restituisce (Mbps, MB scaricati, secondi impiegati) o errore.
        /// </summary>
        public async Task<SpeedTestResult> RunSpeedTestAsync(CancellationToken ct = default)
        {
            try
            {
                using var handler = new System.Net.Http.SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectTimeout = TimeSpan.FromSeconds(15)
                };
                using var client = new System.Net.Http.HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMinutes(2)
                };

                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var response = await client.GetAsync(
                    "https://speed.cloudflare.com/__down?bytes=10485760",
                    System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int n = await stream.ReadAsync(buffer, ct);
                    if (n == 0) break;
                    total += n;
                }
                sw.Stop();

                double secs = sw.Elapsed.TotalSeconds;
                double mbps = secs > 0 ? total * 8.0 / (1_000_000.0 * secs) : 0;
                double mbDown = total / (1024.0 * 1024.0);

                return new SpeedTestResult { Ok = true, Mbps = mbps, MbDownloaded = mbDown, Seconds = secs };
            }
            catch (Exception ex)
            {
                return new SpeedTestResult { Ok = false, Error = ex.Message };
            }
        }

        // ── IP pubblico ──────────────────────────────────────
        /// <summary>Ottiene l'IP pubblico e info base (ISP, città) via API gratuita.</summary>
        public async Task<PublicIpInfo> GetPublicIpAsync(CancellationToken ct = default)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                // ip-api.com: gratuito, no API key, risposta JSON compatta
                string json = await client.GetStringAsync("http://ip-api.com/json/?fields=query,isp,city,regionName,country", ct);

                // Parsing manuale leggero (evita dipendenza System.Text.Json per un solo uso)
                string ip   = ExtractJsonField(json, "query");
                string isp  = ExtractJsonField(json, "isp");
                string city = ExtractJsonField(json, "city");
                string region = ExtractJsonField(json, "regionName");
                string country = ExtractJsonField(json, "country");

                string location = string.Join(", ",
                    new[] { city, region, country }.Where(s => !string.IsNullOrEmpty(s)));

                return new PublicIpInfo { Ok = true, Ip = ip, Isp = isp, Location = location };
            }
            catch (Exception ex)
            {
                return new PublicIpInfo { Ok = false, Error = ex.Message };
            }
        }

        private static string ExtractJsonField(string json, string field)
        {
            // Cerca "field":"value" — parser minimale, basta per ip-api.com
            string key = "\"" + field + "\":\"";
            int start = json.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return "";
            start += key.Length;
            int end = json.IndexOf('"', start);
            return end > start ? json[start..end] : "";
        }

        private void Emit(string msg, string type) => Log?.Invoke(msg, type);
    }

    // ── Result types ─────────────────────────────────────────
    public class SpeedTestResult
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public double Mbps { get; init; }
        public double MbDownloaded { get; init; }
        public double Seconds { get; init; }
    }

    public class PublicIpInfo
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public string Ip { get; init; } = "";
        public string Isp { get; init; } = "";
        public string Location { get; init; } = "";
    }
}