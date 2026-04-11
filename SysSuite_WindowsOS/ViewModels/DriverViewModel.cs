using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.Services;
using Windows.UI;

namespace SysSuite.ViewModels
{
    public partial class DriverViewModel : ObservableObject
    {
        private readonly DriverService _svc;
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DriverViewModel must be created on the UI thread.");

        public ObservableCollection<DriverListRowUi> DriverRows { get; } = new();

        public DriverViewModel(DriverService svc)
        {
            _svc = svc;
            _svc.Log += OnServiceLog;
            _ = InitializeAsync();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BusyRingVisibility))]
        [NotifyPropertyChangedFor(nameof(AreDriverActionsEnabled))]
        private bool _isBusy;

        [ObservableProperty] private string _statusLine = "";

        public Microsoft.UI.Xaml.Visibility BusyRingVisibility =>
            IsBusy ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public bool AreDriverActionsEnabled => !IsBusy;

        private void OnServiceLog(string msg, string type)
        {
            if (type != "err") return;
            EnqueueUi(() => StatusLine = msg);
        }

        private void EnqueueUi(Action action)
        {
            if (_dispatcher.HasThreadAccess)
                action();
            else
                _ = _dispatcher.TryEnqueue(() => action());
        }

        private static DriverListRowUi MapRow(DriverEntry d, bool forceProblemStyle)
        {
            bool problem = forceProblemStyle || d.HasProblem;
            return new DriverListRowUi
            {
                Name = d.Name,
                Manufacturer = d.Manufacturer,
                Version = d.Version,
                Date = d.Date,
                DeviceClass = d.DeviceClass,
                Status = d.HasProblem ? d.Status : "OK",
                StatusColor = problem
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 90, 90))
                    : new SolidColorBrush(Color.FromArgb(255, 52, 211, 153))
            };
        }

        private async Task InitializeAsync()
        {
            IsBusy = true;
            try { await RefreshAllDriversCoreAsync().ConfigureAwait(true); }
            catch (Exception ex) { StatusLine = "Errore: " + ex.Message; }
            finally { IsBusy = false; }
        }

        private async Task RefreshAllDriversCoreAsync()
        {
            var drivers = await _svc.GetDriversAsync().ConfigureAwait(true);
            DriverRows.Clear();
            foreach (var d in drivers)
                DriverRows.Add(MapRow(d, false));
            StatusLine = drivers.Count + " driver";
        }

        [RelayCommand]
        private async Task LoadDriversAsync()
        {
            IsBusy = true;
            try { await RefreshAllDriversCoreAsync().ConfigureAwait(true); }
            catch (Exception ex) { StatusLine = "Errore: " + ex.Message; }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task LoadProblematicAsync()
        {
            IsBusy = true;
            try
            {
                var drivers = await _svc.GetProblematicDriversAsync().ConfigureAwait(true);
                DriverRows.Clear();
                foreach (var d in drivers)
                    DriverRows.Add(MapRow(d, true));
                StatusLine = drivers.Count + " problematici";
            }
            catch (Exception ex) { StatusLine = "Errore: " + ex.Message; }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private async Task BackupDriversAsync()
        {
            if (!AdminHelper.IsAdmin())
            {
                StatusLine = "'Backup Driver (DISM)' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.";
                return;
            }

            IsBusy = true;
            try
            {
                string p = await _svc.BackupDriversAsync().ConfigureAwait(true);
                StatusLine = "Backup: " + p;
            }
            catch (Exception ex) { StatusLine = "Errore: " + ex.Message; }
            finally { IsBusy = false; }
        }
    }
}
