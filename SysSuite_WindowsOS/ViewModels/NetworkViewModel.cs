using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.Services;
using Windows.UI;

namespace SysSuite.ViewModels
{
    public partial class NetworkViewModel : ObservableObject
    {
        private static readonly SolidColorBrush PingOkBrush =
            new(Color.FromArgb(255, 0, 212, 160));

        private static readonly SolidColorBrush PingFailBrush =
            new(Color.FromArgb(255, 255, 90, 90));

        private static readonly SolidColorBrush SpeedFastBrush =
            new(Color.FromArgb(255, 52, 211, 153));

        private static readonly SolidColorBrush SpeedGoodBrush =
            new(Color.FromArgb(255, 59, 158, 255));

        private static readonly SolidColorBrush SpeedMidBrush =
            new(Color.FromArgb(255, 255, 181, 71));

        private static readonly SolidColorBrush SpeedSlowBrush =
            new(Color.FromArgb(255, 255, 90, 90));

        private static readonly SolidColorBrush SpeedDefaultBrush =
            new(Color.FromArgb(255, 59, 158, 255));

        private readonly NetworkService _net;
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("NetworkViewModel must be created on the UI thread.");

        public NetworkViewModel(NetworkService net)
        {
            _net = net;
            _net.Log += OnServiceLog;
            PingResultBrush = PingOkBrush;
            SpeedResultBrush = SpeedDefaultBrush;
        }

        [ObservableProperty] private string _logText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NetworkOptRingVisibility))]
        [NotifyPropertyChangedFor(nameof(AreNetworkOptButtonsEnabled))]
        private bool _isNetworkOptBusy;

        [ObservableProperty] private string _pingHost = "";

        [ObservableProperty] private string _pingResultText = "";

        [ObservableProperty] private SolidColorBrush _pingResultBrush = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PingRingVisibility))]
        [NotifyPropertyChangedFor(nameof(IsPingButtonEnabled))]
        private bool _isPingBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ScanRingVisibility))]
        [NotifyPropertyChangedFor(nameof(IsScanButtonEnabled))]
        private bool _isScanBusy;

        [ObservableProperty] private double _scanProgress;

        [ObservableProperty] private string _scanStatusText = "";

        public ObservableCollection<PortEntry> PortEntries { get; } = new();

        [ObservableProperty] private string _publicIpText = "—";

        [ObservableProperty] private string _ipIspText = "";

        [ObservableProperty] private string _ipLocationText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCheckIpButtonEnabled))]
        private bool _isCheckIpBusy;

        [ObservableProperty] private string _speedResultText = "—";

        [ObservableProperty] private string _speedDetailText =
            "Scarica ~10 MB da Cloudflare per misurare la banda.";

        [ObservableProperty] private SolidColorBrush _speedResultBrush = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSpeedTestButtonEnabled))]
        private bool _isSpeedTestBusy;

        public Visibility NetworkOptRingVisibility =>
            IsNetworkOptBusy ? Visibility.Visible : Visibility.Collapsed;

        public bool AreNetworkOptButtonsEnabled => !IsNetworkOptBusy;

        public Visibility PingRingVisibility =>
            IsPingBusy ? Visibility.Visible : Visibility.Collapsed;

        public bool IsPingButtonEnabled => !IsPingBusy;

        public Visibility ScanRingVisibility =>
            IsScanBusy ? Visibility.Visible : Visibility.Collapsed;

        public bool IsScanButtonEnabled => !IsScanBusy;

        public bool IsCheckIpButtonEnabled => !IsCheckIpBusy;

        public bool IsSpeedTestButtonEnabled => !IsSpeedTestBusy;

        private void OnServiceLog(string msg, string type) =>
            EnqueueUi(() => AppendLogLine(msg, type));

        private void AppendLogLine(string msg, string type)
        {
            LogText += "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n";
        }

        private void EnqueueUi(Action action)
        {
            if (_dispatcher.HasThreadAccess)
                action();
            else
                _ = _dispatcher.TryEnqueue(() => action());
        }

        private static bool CheckAdmin(string label, Action<string, string> log)
        {
            if (AdminHelper.IsAdmin()) return true;
            log("'" + label + "' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.", "warn");
            return false;
        }

        private async Task RunNetworkOptAsync(Func<Task> work)
        {
            IsNetworkOptBusy = true;
            try { await work().ConfigureAwait(true); }
            catch (Exception ex) { AppendLogLine(ex.Message, "err"); }
            finally { IsNetworkOptBusy = false; }
        }

        [RelayCommand]
        private async Task FlushDnsAsync()
        {
            if (!CheckAdmin("Svuota DNS", AppendLogLine)) return;
            await RunNetworkOptAsync(() => _net.FlushDNSAsync());
        }

        [RelayCommand]
        private async Task ResetTcpAsync()
        {
            if (!CheckAdmin("Reset TCP/IP", AppendLogLine)) return;
            await RunNetworkOptAsync(() => _net.ResetTCPIPAsync());
        }

        [RelayCommand]
        private async Task ResetWinsockAsync()
        {
            if (!CheckAdmin("Reset Winsock", AppendLogLine)) return;
            await RunNetworkOptAsync(() => _net.ResetWinsockAsync());
        }

        [RelayCommand]
        private async Task DnsGoogleAsync()
        {
            if (!CheckAdmin("DNS Google", AppendLogLine)) return;
            await RunNetworkOptAsync(() => _net.SetDNSAsync("8.8.8.8", "8.8.4.4", "Google"));
        }

        [RelayCommand]
        private async Task DnsCloudflareAsync()
        {
            if (!CheckAdmin("DNS Cloudflare", AppendLogLine)) return;
            await RunNetworkOptAsync(() => _net.SetDNSAsync("1.1.1.1", "1.0.0.1", "Cloudflare"));
        }

        [RelayCommand]
        private async Task DnsDhcpAsync()
        {
            if (!CheckAdmin("Ripristina DNS automatico", AppendLogLine)) return;
            await RunNetworkOptAsync(() => _net.SetDNSAsync("", "", "DHCP"));
        }

        [RelayCommand]
        private async Task NagleOffAsync()
        {
            if (!CheckAdmin("Nagle OFF", AppendLogLine)) return;
            await RunNetworkOptAsync(() =>
            {
                _net.DisableNagle();
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task NagleRestoreAsync()
        {
            if (!CheckAdmin("Ripristina Nagle", AppendLogLine)) return;
            await RunNetworkOptAsync(() =>
            {
                _net.RestoreNagle();
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task PingAsync()
        {
            string host = PingHost.Trim();
            if (string.IsNullOrEmpty(host))
            {
                PingResultText = "Inserisci un host.";
                return;
            }

            if (host.Length > 253 || !Regex.IsMatch(host, @"^[-a-zA-Z0-9.:_%]+$"))
            {
                PingResultText = "Host non valido.";
                return;
            }

            PingResultText = "...";
            IsPingBusy = true;
            try
            {
                double ms = await _net.PingAsync(host).ConfigureAwait(true);
                PingResultText = ms >= 0
                    ? host + "  →  " + ms + " ms"
                    : host + "  →  timeout / non raggiungibile";
                PingResultBrush = ms >= 0 ? PingOkBrush : PingFailBrush;
            }
            catch (Exception ex)
            {
                PingResultText = "Errore: " + ex.Message;
            }
            finally
            {
                IsPingBusy = false;
            }
        }

        [RelayCommand]
        private async Task ScanPortsAsync()
        {
            IsScanBusy = true;
            ScanProgress = 0;
            PortEntries.Clear();
            ScanStatusText = "Scansione in corso...";
            var progress = new Progress<int>(v =>
            {
                if (_dispatcher.HasThreadAccess)
                    ScanProgress = v;
                else
                    _ = _dispatcher.TryEnqueue(() => ScanProgress = v);
            });

            try
            {
                var ports = await _net.ScanPortsAsync(progress).ConfigureAwait(true);
                PortEntries.Clear();
                foreach (var p in ports)
                    PortEntries.Add(p);
                ScanStatusText = ports.Count + " porte aperte trovate.";
            }
            catch (Exception ex)
            {
                AppendLogLine(ex.Message, "err");
            }
            finally
            {
                IsScanBusy = false;
            }
        }

        [RelayCommand]
        private async Task SpeedTestAsync()
        {
            IsSpeedTestBusy = true;
            SpeedResultText = "Test in corso...";
            SpeedDetailText = "Download di ~10 MB da Cloudflare...";

            try
            {
                var result = await _net.RunSpeedTestAsync().ConfigureAwait(true);
                if (result.Ok)
                {
                    SpeedResultText = result.Mbps.ToString("0.#") + " Mbps";
                    SpeedDetailText = result.MbDownloaded.ToString("0.#") + " MB in "
                        + result.Seconds.ToString("0.#") + " secondi";

                    SpeedResultBrush = result.Mbps >= 100 ? SpeedFastBrush
                        : result.Mbps >= 30 ? SpeedGoodBrush
                        : result.Mbps >= 10 ? SpeedMidBrush
                        : SpeedSlowBrush;

                    AppendLogLine($"Speed test: {result.Mbps:0.#} Mbps ({result.MbDownloaded:0.#} MB in {result.Seconds:0.#}s)", "ok");
                }
                else
                {
                    SpeedResultText = "Errore";
                    SpeedDetailText = result.Error ?? "Connessione fallita";
                    SpeedResultBrush = SpeedSlowBrush;
                    AppendLogLine("Speed test fallito: " + result.Error, "err");
                }
            }
            catch (Exception ex)
            {
                SpeedResultText = "Errore";
                SpeedDetailText = ex.Message;
                AppendLogLine("Speed test: " + ex.Message, "err");
            }
            finally
            {
                IsSpeedTestBusy = false;
            }
        }

        [RelayCommand]
        private async Task CheckPublicIpAsync()
        {
            IsCheckIpBusy = true;
            PublicIpText = "...";
            IpIspText = "";
            IpLocationText = "";

            try
            {
                var info = await _net.GetPublicIpAsync().ConfigureAwait(true);
                if (info.Ok)
                {
                    PublicIpText = info.Ip;
                    IpIspText = !string.IsNullOrEmpty(info.Isp) ? "ISP: " + info.Isp : "";
                    IpLocationText = !string.IsNullOrEmpty(info.Location) ? info.Location : "";
                    AppendLogLine("IP pubblico: " + info.Ip + " (" + info.Isp + ")", "ok");
                }
                else
                {
                    PublicIpText = "Errore";
                    IpIspText = info.Error ?? "";
                    AppendLogLine("IP check fallito: " + info.Error, "err");
                }
            }
            catch (Exception ex)
            {
                PublicIpText = "Errore";
                IpIspText = ex.Message;
            }
            finally
            {
                IsCheckIpBusy = false;
            }
        }

        [RelayCommand]
        private void ClearLog() => LogText = "";
    }
}
