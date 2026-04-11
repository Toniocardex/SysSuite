using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SysSuite.Services;
using Windows.UI;

namespace SysSuite.Views
{
    public sealed partial class BrowserPage : Page
    {
        private readonly BrowserService _svc = new();
        private List<BrowserService.BrowserInfo> _browsers = new();

        public BrowserPage()
        {
            InitializeComponent();
            _svc.Log += AppendLog;
            Loaded += (_, _) => { _ = DetectAsync(); };
        }

        private void SetBrowserWorkBusy(bool busy)
        {
            RingBrowserBusy.IsActive = busy;
            RingBrowserBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            BtnDetectBrowsers.IsEnabled = !busy;
            BtnCleanCache.IsEnabled = !busy;
            BtnCleanDeep.IsEnabled = !busy;
            LvBrowsers.IsEnabled = !busy;
        }

        private async Task DetectAsync()
        {
            SetBrowserWorkBusy(true);
            try
            {
                _browsers = await _svc.DetectBrowsersAsync().ConfigureAwait(true);
                var rows = new List<object>();
                foreach (var b in _browsers)
                {
                    long bytes = b.Installed ? await _svc.GetCacheSizeAsync(b).ConfigureAwait(true) : 0;
                    string sizeStr = bytes == 0 ? "—"
                        : bytes >= 1_048_576 ? (bytes / 1_048_576) + " MB"
                        : (bytes / 1_024) + " KB";
                    rows.Add(new
                    {
                        b.Name,
                        b.CachePath,
                        CacheSize = sizeStr,
                        InstalledText = b.Installed ? "RILEVATO" : "non trovato",
                        InstalledColor = b.Installed
                            ? new SolidColorBrush(Color.FromArgb(255, 52, 211, 153))
                            : new SolidColorBrush(Color.FromArgb(255, 61, 77, 102))
                    });
                }

                LvBrowsers.ItemsSource = rows;
                AppendLog(_browsers.Count(b => b.Installed) + " browser rilevati.", "ok");
            }
            catch (Exception ex) { AppendLog("Rilevamento: " + ex.Message, "err"); }
            finally { SetBrowserWorkBusy(false); }
        }

        private async void BtnDetect_Click(object s, RoutedEventArgs e)
        {
            await DetectAsync();
        }

        private void BtnClear_Click(object s, RoutedEventArgs e) => TxtLog.Text = "";

        private async void BtnCleanDeep_Click(object s, RoutedEventArgs e)
        {
            SetBrowserWorkBusy(true);
            try
            {
                foreach (var b in _browsers.Where(b => b.Installed))
                {
                    try { await _svc.CleanAllAsync(b).ConfigureAwait(true); }
                    catch (Exception ex) { AppendLog(b.Name + ": " + ex.Message, "err"); }
                }

                await DetectAsync();
            }
            finally { SetBrowserWorkBusy(false); }
        }

        private async void BtnCleanAll_Click(object s, RoutedEventArgs e)
        {
            SetBrowserWorkBusy(true);
            try
            {
                foreach (var b in _browsers.Where(b => b.Installed))
                {
                    try { await _svc.CleanCacheAsync(b).ConfigureAwait(true); }
                    catch (Exception ex) { AppendLog($"{b.Name}: {ex.Message}", "err"); }
                }

                await DetectAsync();
            }
            finally { SetBrowserWorkBusy(false); }
        }

        private void AppendLog(string msg, string type) =>
            DispatcherQueue.TryEnqueue(() => TxtLog.Text += "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n");
    }
}
