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

        public BrowserPage() { InitializeComponent(); _svc.Log += AppendLog; Loaded += (_, _) => Detect(); }

        private void BtnDetect_Click(object s, RoutedEventArgs e) => Detect();
        private void BtnClear_Click(object s, RoutedEventArgs e) => TxtLog.Text = "";

        private void Detect()
        {
            _browsers = _svc.DetectBrowsers();
            LvBrowsers.ItemsSource = _browsers.Select(b =>
            {
                long bytes = b.Installed ? _svc.GetCacheSize(b) : 0;
                string sizeStr = bytes == 0 ? "—"
                    : bytes >= 1_048_576 ? (bytes / 1_048_576) + " MB"
                    : (bytes / 1_024) + " KB";
                return new
                {
                    b.Name, b.CachePath,
                    CacheSize     = sizeStr,
                    InstalledText = b.Installed ? "RILEVATO" : "non trovato",
                    InstalledColor = b.Installed
                        ? new SolidColorBrush(Color.FromArgb(255,52,211,153))
                        : new SolidColorBrush(Color.FromArgb(255,61,77,102))
                };
            }).ToList();
            AppendLog(_browsers.Count(b => b.Installed) + " browser rilevati.", "ok");
        }

        private void BtnCleanDeep_Click(object s, RoutedEventArgs e)
        {
            foreach (var b in _browsers.Where(b => b.Installed))
                try { _svc.CleanAll(b); } catch (Exception ex) { AppendLog(b.Name + ": " + ex.Message, "err"); }
            Detect();
        }

        private void BtnCleanAll_Click(object s, RoutedEventArgs e)
        {
            foreach (var b in _browsers.Where(b => b.Installed))
                try { _svc.CleanCache(b); } catch (Exception ex) { AppendLog($"{b.Name}: {ex.Message}", "err"); }
        }

        private void AppendLog(string msg, string type) =>
            DispatcherQueue.TryEnqueue(() => TxtLog.Text += "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n");
    }
}