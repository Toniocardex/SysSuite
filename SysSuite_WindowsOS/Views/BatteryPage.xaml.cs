using System.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using SysSuite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SysSuite.Models;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class BatteryPage : Page
    {
        public BatteryViewModel ViewModel { get; }

        private DispatcherQueueTimer? _timer;

        public BatteryPage()
        {
            InitializeComponent();
            ViewModel = App.Services.GetRequiredService<BatteryViewModel>();
            DataContext = ViewModel;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            Loaded += async (_, _) =>
            {
                await ViewModel.InitializeAsync();
                SyncDonutsFromViewModel();
                _timer = DispatcherQueue.CreateTimer();
                _timer.Interval = TimeSpan.FromSeconds(30);
                _timer.Tick += async (_, _) =>
                {
                    try { await ViewModel.SilentRefreshAsync(); }
                    catch { }
                };
                _timer.Start();
            };
            Unloaded += (_, _) => _timer?.Stop();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatteryViewModel.BatteryInfo))
                SyncDonutsFromViewModel();
        }

        private void SyncDonutsFromViewModel()
        {
            var info = ViewModel.BatteryInfo;
            if (info is null)
                return;

            UpdateDonut(ChartHealth, info.HealthPercent, new SKColor(52, 211, 153), new SKColor(20, 24, 40));
            UpdateDonut(ChartCharge, info.ChargePercent, new SKColor(59, 158, 255), new SKColor(20, 24, 40));
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
                    Values = new double[] { valuePct },
                    InnerRadius = 36,
                    Fill = new SolidColorPaint(color),
                    Stroke = null,
                    Pushout = 0,
                    HoverPushout = 0,
                    DataLabelsPaint = null
                },
                new PieSeries<double>
                {
                    Values = new double[] { 100 - valuePct },
                    InnerRadius = 36,
                    Fill = new SolidColorPaint(background),
                    Stroke = null,
                    Pushout = 0,
                    HoverPushout = 0,
                    IsHoverable = false,
                    DataLabelsPaint = null
                }
            };
        }
    }
}
