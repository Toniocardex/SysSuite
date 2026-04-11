using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.Views
{
    public sealed partial class BatteryPage : Page
    {
        private readonly BatteryService _svc = new();
        private DispatcherQueueTimer? _timer;

        public BatteryPage()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                await RefreshDataAsync(showBusy: true);
                _timer = DispatcherQueue.CreateTimer();
                _timer.Interval = TimeSpan.FromSeconds(30);
                _timer.Tick += async (_, _) =>
                {
                    try { await RefreshDataAsync(showBusy: false); }
                    catch { /* timer: non disturbare l'utente */ }
                };
                _timer.Start();
            };
            Unloaded += (_, _) => _timer?.Stop();
        }

        private void SetBatteryActionsBusy(bool busy)
        {
            RingBatteryBusy.IsActive = busy;
            RingBatteryBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            BtnRefreshBattery.IsEnabled = !busy;
            BtnBatteryReport.IsEnabled = !busy;
        }

        private async Task RefreshDataAsync(bool showBusy)
        {
            if (showBusy)
                SetBatteryActionsBusy(true);
            try
            {
                var info = await _svc.GetBatteryInfoAsync().ConfigureAwait(true);
                ApplyBatteryUi(info);
            }
            catch (Exception ex)
            {
                TxtResult.Text = "Errore: " + ex.Message;
            }
            finally
            {
                if (showBusy)
                    SetBatteryActionsBusy(false);
            }
        }

        private void ApplyBatteryUi(BatteryInfo? info)
        {
            if (info == null)
            {
                TxtNoBattery.Visibility = Visibility.Visible;
                GridBattery.Visibility = Visibility.Collapsed;
                return;
            }

            TxtNoBattery.Visibility = Visibility.Collapsed;
            GridBattery.Visibility = Visibility.Visible;

            TxtHealth.Text = info.HealthPercent + "%";
            UpdateDonut(ChartHealth,
                info.HealthPercent,
                new SKColor(52, 211, 153),
                new SKColor(20, 24, 40));

            TxtCharge.Text = info.ChargePercent + "%";
            UpdateDonut(ChartCharge,
                info.ChargePercent,
                new SKColor(59, 158, 255),
                new SKColor(20, 24, 40));

            TxtBattName.Text = info.Name;
            TxtStatus.Text = "Stato: " + info.Status;
        }

        private static void UpdateDonut(
            LiveChartsCore.SkiaSharpView.WinUI.PieChart chart,
            double valuePct,
            SKColor color,
            SKColor background)
        {
            valuePct = Math.Clamp(valuePct, 0, 100);
            chart.Series = new ISeries[]
            {
                new PieSeries<double>
                {
                    Values       = new double[] { valuePct },
                    InnerRadius  = 36,
                    Fill         = new SolidColorPaint(color),
                    Stroke       = null,
                    Pushout      = 0,
                    HoverPushout = 0,
                    DataLabelsPaint = null
                },
                new PieSeries<double>
                {
                    Values       = new double[] { 100 - valuePct },
                    InnerRadius  = 36,
                    Fill         = new SolidColorPaint(background),
                    Stroke       = null,
                    Pushout      = 0,
                    HoverPushout = 0,
                    IsHoverable  = false,
                    DataLabelsPaint = null
                }
            };
        }

        private async void BtnRefresh_Click(object s, RoutedEventArgs e)
        {
            SetBatteryActionsBusy(true);
            try
            {
                var info = await _svc.GetBatteryInfoAsync().ConfigureAwait(true);
                ApplyBatteryUi(info);
            }
            catch (Exception ex) { TxtResult.Text = "Errore: " + ex.Message; }
            finally { SetBatteryActionsBusy(false); }
        }

        private async void BtnReport_Click(object s, RoutedEventArgs e)
        {
            SetBatteryActionsBusy(true);
            try
            {
                string path = await _svc.GenerateReportAsync().ConfigureAwait(true);
                TxtResult.Text = "Report salvato: " + path;
            }
            catch (Exception ex) { TxtResult.Text = "Errore: " + ex.Message; }
            finally { SetBatteryActionsBusy(false); }
        }
    }
}
