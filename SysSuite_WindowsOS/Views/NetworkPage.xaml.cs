using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Core;
using SysSuite.Services;

namespace SysSuite.Views
{
    public sealed partial class NetworkPage : Page
    {
        private readonly NetworkService _net = new();

        public NetworkPage()
        {
            InitializeComponent();
            _net.Log += AppendLog;
        }

        // Richiedono admin (netsh, ipconfig /flushdns, HKLM)
        private void BtnFlushDNS_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Svuota DNS")) return;
            try { _net.FlushDNS(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnResetTCP_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Reset TCP/IP")) return;
            try { _net.ResetTCPIP(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnResetWinsock_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Reset Winsock")) return;
            try { _net.ResetWinsock(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnDNSGoogle_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("DNS Google")) return;
            try { _net.SetDNS("8.8.8.8", "8.8.4.4", "Google"); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnDNSCF_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("DNS Cloudflare")) return;
            try { _net.SetDNS("1.1.1.1", "1.0.0.1", "Cloudflare"); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }
        private void BtnDNSDHCP_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Ripristina DNS automatico")) return;
            try { _net.SetDNS("", "", "DHCP"); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

        private void BtnNagleRestore_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Ripristina Nagle")) return;
            try { _net.RestoreNagle(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            string host = TxtPingHost.Text.Trim();
            if (string.IsNullOrEmpty(host)) { TxtPingResult.Text = "Inserisci un host."; return; }
            // Validazione: solo hostname, FQDN o indirizzo (niente shell metacharacters)
            if (host.Length > 253 || !System.Text.RegularExpressions.Regex.IsMatch(host, @"^[-a-zA-Z0-9.:_%]+$"))
            {
                TxtPingResult.Text = "Host non valido.";
                return;
            }
            TxtPingResult.Text = "...";
            ((Button)sender).IsEnabled = false;
            try
            {
                double ms = await _net.PingAsync(host);
                TxtPingResult.Text = ms >= 0
                    ? host + "  →  " + ms + " ms"
                    : host + "  →  timeout / non raggiungibile";
                TxtPingResult.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    ms >= 0
                        ? Windows.UI.Color.FromArgb(255, 0, 212, 160)
                        : Windows.UI.Color.FromArgb(255, 255, 90, 90));
            }
            catch (Exception ex) { TxtPingResult.Text = "Errore: " + ex.Message; }
            finally { ((Button)sender).IsEnabled = true; }
        }

        private void BtnNagleOff_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Nagle OFF")) return;
            try { _net.DisableNagle(); } catch (Exception ex) { AppendLog(ex.Message, "err"); }
        }

        // Scanner porte — NO admin (connessione TCP locale)
        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            ((Button)sender).IsEnabled = false;
            LvPorts.ItemsSource = null;
            TxtScanStatus.Text = "Scansione in corso...";
            var progress = new Progress<int>(v => { PbScan.Value = v; });
            try
            {
                var ports = await _net.ScanPortsAsync(progress);
                LvPorts.ItemsSource = ports;
                TxtScanStatus.Text = ports.Count + " porte aperte trovate.";
            }
            catch (Exception ex) { AppendLog(ex.Message, "err"); }
            finally { ((Button)sender).IsEnabled = true; }
        }

        private void BtnClear_Click(object s, RoutedEventArgs e) => TxtLog.Text = "";

        // ── Speed Test ───────────────────────────────────────
        private async void BtnSpeedTest_Click(object sender, RoutedEventArgs e)
        {
            BtnSpeedTest.IsEnabled = false;
            PbSpeed.IsIndeterminate = true;
            TxtSpeedResult.Text = "Test in corso...";
            TxtSpeedDetail.Text = "Download di ~10 MB da Cloudflare...";

            try
            {
                var result = await _net.RunSpeedTestAsync();
                if (result.Ok)
                {
                    TxtSpeedResult.Text = result.Mbps.ToString("0.#") + " Mbps";
                    TxtSpeedDetail.Text = result.MbDownloaded.ToString("0.#") + " MB in "
                        + result.Seconds.ToString("0.#") + " secondi";

                    // Colore basato sulla velocità
                    TxtSpeedResult.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        result.Mbps >= 100 ? Windows.UI.Color.FromArgb(255, 52, 211, 153)   // verde
                        : result.Mbps >= 30 ? Windows.UI.Color.FromArgb(255, 59, 158, 255)   // blu
                        : result.Mbps >= 10 ? Windows.UI.Color.FromArgb(255, 255, 181, 71)   // ambra
                        :                     Windows.UI.Color.FromArgb(255, 255, 90, 90));   // rosso

                    AppendLog($"Speed test: {result.Mbps:0.#} Mbps ({result.MbDownloaded:0.#} MB in {result.Seconds:0.#}s)", "ok");
                }
                else
                {
                    TxtSpeedResult.Text = "Errore";
                    TxtSpeedDetail.Text = result.Error ?? "Connessione fallita";
                    TxtSpeedResult.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 255, 90, 90));
                    AppendLog("Speed test fallito: " + result.Error, "err");
                }
            }
            catch (Exception ex)
            {
                TxtSpeedResult.Text = "Errore";
                TxtSpeedDetail.Text = ex.Message;
                AppendLog("Speed test: " + ex.Message, "err");
            }
            finally
            {
                PbSpeed.IsIndeterminate = false;
                BtnSpeedTest.IsEnabled = true;
            }
        }

        // ── IP Pubblico ──────────────────────────────────────
        private async void BtnCheckIp_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckIp.IsEnabled = false;
            TxtPublicIp.Text = "...";
            TxtIpIsp.Text = "";
            TxtIpLocation.Text = "";

            try
            {
                var info = await _net.GetPublicIpAsync();
                if (info.Ok)
                {
                    TxtPublicIp.Text = info.Ip;
                    TxtIpIsp.Text = !string.IsNullOrEmpty(info.Isp) ? "ISP: " + info.Isp : "";
                    TxtIpLocation.Text = !string.IsNullOrEmpty(info.Location) ? info.Location : "";
                    AppendLog("IP pubblico: " + info.Ip + " (" + info.Isp + ")", "ok");
                }
                else
                {
                    TxtPublicIp.Text = "Errore";
                    TxtIpIsp.Text = info.Error ?? "";
                    AppendLog("IP check fallito: " + info.Error, "err");
                }
            }
            catch (Exception ex)
            {
                TxtPublicIp.Text = "Errore";
                TxtIpIsp.Text = ex.Message;
            }
            finally { BtnCheckIp.IsEnabled = true; }
        }

        private void AppendLog(string msg, string type) =>
            DispatcherQueue.TryEnqueue(() =>
                TxtLog.Text += "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n");

        private bool CheckAdmin(string label)
        {
            if (AdminHelper.IsAdmin()) return true;
            AppendLog("'" + label + "' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
            return false;
        }
    }
}