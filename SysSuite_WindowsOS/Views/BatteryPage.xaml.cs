using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
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
            Loaded += (_, _) =>
            {
                Refresh();
                _timer = DispatcherQueue.CreateTimer();
                _timer.Interval = TimeSpan.FromSeconds(30);
                _timer.Tick += (_, _) => Refresh();
                _timer.Start();
            };
            Unloaded += (_, _) => _timer?.Stop();
        }

        private void BtnRefresh_Click(object s, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            var info = _svc.GetBatteryInfo();
            if (info == null)
            {
                TxtNoBattery.Visibility = Visibility.Visible;
                GridBattery.Visibility  = Visibility.Collapsed;
                return;
            }

            TxtNoBattery.Visibility = Visibility.Collapsed;
            GridBattery.Visibility  = Visibility.Visible;

            // Donut Salute
            TxtHealth.Text = info.HealthPercent + "%";
            UpdateDonut(ChartHealth,
                info.HealthPercent,
                new SKColor(52, 211, 153),   // #34D399 verde
                new SKColor(20, 24, 40));

            // Donut Carica
            TxtCharge.Text = info.ChargePercent + "%";
            UpdateDonut(ChartCharge,
                info.ChargePercent,
                new SKColor(59, 158, 255),   // #3B9EFF blu
                new SKColor(20, 24, 40));

            TxtBattName.Text = info.Name;
            TxtStatus.Text   = "Stato: " + info.Status;
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

        private void BtnReport_Click(object s, RoutedEventArgs e)
        {
            try   { var path = _svc.GenerateReport(); TxtResult.Text = "Report salvato: " + path; }
            catch (Exception ex) { TxtResult.Text = "Errore: " + ex.Message; }
        }
    }
}