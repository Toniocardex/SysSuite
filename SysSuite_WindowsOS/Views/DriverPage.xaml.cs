using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SysSuite.Core;
using SysSuite.Models;
using SysSuite.Services;
using Windows.UI;

namespace SysSuite.Views
{
    public sealed partial class DriverPage : Page
    {
        private readonly DriverService _svc = new();

        public DriverPage()
        {
            InitializeComponent();
            _svc.Log += OnServiceLog;
            Loaded += (_, _) => { _ = InitializeAsync(); };
        }

        private void OnServiceLog(string msg, string type) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                if (type == "err")
                    TxtCount.Text = msg;
            });

        private void SetDriverUiBusy(bool busy)
        {
            RingDriverBusy.IsActive = busy;
            RingDriverBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            BtnLoadDrivers.IsEnabled = !busy;
            BtnProblematic.IsEnabled = !busy;
            BtnBackupDrivers.IsEnabled = !busy;
            LvDrivers.IsEnabled = !busy;
        }

        private async Task InitializeAsync()
        {
            SetDriverUiBusy(true);
            try { await RefreshAllDriversAsync(); }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
            finally { SetDriverUiBusy(false); }
        }

        private static object MapDriverRow(DriverEntry d, bool forceProblemStyle)
        {
            bool problem = forceProblemStyle || d.HasProblem;
            return new
            {
                d.Name,
                d.Manufacturer,
                d.Version,
                d.Date,
                d.DeviceClass,
                Status = d.HasProblem ? d.Status : "OK",
                StatusColor = problem
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 90, 90))
                    : new SolidColorBrush(Color.FromArgb(255, 52, 211, 153))
            };
        }

        private async Task RefreshAllDriversAsync()
        {
            var drivers = await _svc.GetDriversAsync().ConfigureAwait(true);
            LvDrivers.ItemsSource = drivers.Select(d => MapDriverRow(d, false)).ToList();
            TxtCount.Text = drivers.Count + " driver";
        }

        // Lettura driver — NO admin
        private async void BtnLoad_Click(object s, RoutedEventArgs e)
        {
            SetDriverUiBusy(true);
            try
            {
                await RefreshAllDriversAsync();
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
            finally { SetDriverUiBusy(false); }
        }

        private async void BtnProblematic_Click(object s, RoutedEventArgs e)
        {
            SetDriverUiBusy(true);
            try
            {
                var drivers = await _svc.GetProblematicDriversAsync().ConfigureAwait(true);
                LvDrivers.ItemsSource = drivers.Select(d => MapDriverRow(d, true)).ToList();
                TxtCount.Text = drivers.Count + " problematici";
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
            finally { SetDriverUiBusy(false); }
        }

        // Backup DISM — admin
        private async void BtnBackup_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Backup Driver (DISM)")) return;
            SetDriverUiBusy(true);
            try
            {
                string p = await _svc.BackupDriversAsync().ConfigureAwait(true);
                TxtCount.Text = "Backup: " + p;
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
            finally { SetDriverUiBusy(false); }
        }

        private bool CheckAdmin(string label)
        {
            if (AdminHelper.IsAdmin()) return true;
            TxtCount.Text = "'" + label + "' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.";
            return false;
        }
    }
}
