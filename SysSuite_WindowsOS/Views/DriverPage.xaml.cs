using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SysSuite.Core;
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
            _svc.Log += (m, t) => System.Diagnostics.Debug.WriteLine("[" + t + "] " + m);
            Loaded += (_, _) => BtnLoad_Click(this, new RoutedEventArgs());
        }

        // Lettura driver — NO admin
        private void BtnLoad_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var drivers = _svc.GetDrivers();
                LvDrivers.ItemsSource = drivers.Select(d => new
                {
                    d.Name, d.Manufacturer, d.Version, d.Date, d.DeviceClass,
                    Status = d.HasProblem ? d.Status : "OK",
                    StatusColor = d.HasProblem
                        ? new SolidColorBrush(Color.FromArgb(255, 255, 90, 90))
                        : new SolidColorBrush(Color.FromArgb(255, 52, 211, 153))
                }).ToList();
                TxtCount.Text = drivers.Count + " driver";
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
        }

        private void BtnProblematic_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var drivers = _svc.GetProblematicDrivers();
                LvDrivers.ItemsSource = drivers.Select(d => new
                {
                    d.Name, d.Manufacturer, d.Version, d.Date, d.DeviceClass,
                    Status = d.Status,
                    StatusColor = new SolidColorBrush(Color.FromArgb(255, 255, 90, 90))
                }).ToList();
                TxtCount.Text = drivers.Count + " problematici";
            }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
        }

        // Backup DISM — admin
        private void BtnBackup_Click(object s, RoutedEventArgs e)
        {
            if (!CheckAdmin("Backup Driver (DISM)")) return;
            try { var p = _svc.BackupDrivers(); TxtCount.Text = "Backup: " + p; }
            catch (Exception ex) { TxtCount.Text = "Errore: " + ex.Message; }
        }

        private bool CheckAdmin(string label)
        {
            if (AdminHelper.IsAdmin()) return true;
            TxtCount.Text = "'" + label + "' richiede Admin. Usa 'Riavvia come Admin' in basso a destra.";
            return false;
        }
    }
}