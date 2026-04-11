using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    public partial class BatteryViewModel : ObservableObject
    {
        private readonly BatteryService _batteryService;

        public BatteryViewModel(BatteryService batteryService)
        {
            _batteryService = batteryService;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HealthPercentText))]
        [NotifyPropertyChangedFor(nameof(ChargePercentText))]
        [NotifyPropertyChangedFor(nameof(BatteryNameText))]
        [NotifyPropertyChangedFor(nameof(StatusDescriptionText))]
        [NotifyPropertyChangedFor(nameof(NoBatteryPanelVisibility))]
        [NotifyPropertyChangedFor(nameof(BatteryGridVisibility))]
        private BatteryInfo? _batteryInfo;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BusyRingVisibility))]
        private bool _isBusy;

        [ObservableProperty] private string _resultMessage = "";

        public Visibility NoBatteryPanelVisibility =>
            BatteryInfo is null ? Visibility.Visible : Visibility.Collapsed;

        public Visibility BatteryGridVisibility =>
            BatteryInfo is null ? Visibility.Collapsed : Visibility.Visible;

        public Visibility BusyRingVisibility =>
            IsBusy ? Visibility.Visible : Visibility.Collapsed;

        public string HealthPercentText =>
            BatteryInfo is null ? "—" : BatteryInfo.HealthPercent + "%";

        public string ChargePercentText =>
            BatteryInfo is null ? "—" : BatteryInfo.ChargePercent + "%";

        public string BatteryNameText => BatteryInfo?.Name ?? "—";

        public string StatusDescriptionText =>
            BatteryInfo is null ? "—" : "Stato: " + BatteryInfo.Status;

        public Task InitializeAsync() => LoadBatteryAsync(showBusy: true);

        public Task SilentRefreshAsync() => LoadBatteryAsync(showBusy: false);

        private async Task LoadBatteryAsync(bool showBusy)
        {
            if (showBusy)
                IsBusy = true;
            try
            {
                BatteryInfo = await _batteryService.GetBatteryInfoAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ResultMessage = "Errore: " + ex.Message;
            }
            finally
            {
                if (showBusy)
                    IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RefreshAsync() => await LoadBatteryAsync(true);

        [RelayCommand]
        private async Task GenerateReportAsync()
        {
            IsBusy = true;
            try
            {
                string path = await _batteryService.GenerateReportAsync().ConfigureAwait(true);
                ResultMessage = "Report salvato: " + path;
            }
            catch (Exception ex)
            {
                ResultMessage = "Errore: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
