using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysSuite.Core;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SysSuite.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
            SliderDisk.ValueChanged += (_, e) =>
                LblDiskPct.Text = "Soglia disco: " + (int)e.NewValue + "%";
            SliderCpu.ValueChanged += (_, e) =>
                LblCpuPct.Text = "Soglia CPU: " + (int)e.NewValue + "%";
            SliderRam.ValueChanged += (_, e) =>
                LblRamPct.Text = "Soglia RAM: " + (int)e.NewValue + "%";
            Loaded += (_, _) => LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            var s = SettingsService.Load();
            TogRestoreLastTab.IsOn = s.RestoreLastTabOnStartup;
            RefreshDndStatus();

            TogNotifyDisk.IsOn = s.NotifyDiskFull;
            TogNotifyCpu.IsOn = s.NotifyHighCPU;
            TogNotifyRam.IsOn = s.NotifyHighRAM;
            TogNotifyDriver.IsOn = s.NotifyDriverProblems;
            SliderDisk.Value = s.DiskWarningPct;
            SliderCpu.Value = s.CpuWarningPct;
            SliderRam.Value = s.RamWarningPct;
            LblDiskPct.Text = "Soglia disco: " + (int)s.DiskWarningPct + "%";
            LblCpuPct.Text = "Soglia CPU: " + (int)s.CpuWarningPct + "%";
            LblRamPct.Text = "Soglia RAM: " + (int)s.RamWarningPct + "%";

            TogBoostTemp.IsOn = s.BoostCleanTemp;
            TogBoostThumb.IsOn = s.BoostCleanThumbnails;
            TogBoostBrowser.IsOn = s.BoostCleanBrowserCache;
            TogBoostKill.IsOn = s.BoostKillNotResponding;

            TxtSettingsPath.Text = SettingsService.SettingsFilePath;

            var asm = Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version;
            string ver = v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
            TxtAbout.Text =
                "SysSuite One v" + ver + " · Copyright Antonio Cardelli · Licenza MIT." + Environment.NewLine +
                "Nessun invio dati in rete: le impostazioni restano solo su questo PC.";
        }

        private void RefreshDndStatus()
        {
            var s = SettingsService.Load();
            if (s.DndUntil.HasValue && s.DndUntil.Value > DateTime.Now)
                TxtDndStatus.Text = "Non disturbare attivo fino alle " + s.DndUntil.Value.ToString("HH:mm") +
                    " del " + s.DndUntil.Value.ToString("dd/MM/yyyy");
            else
                TxtDndStatus.Text = "Non disturbare: disattivo";
        }

        private void BtnDnd30_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Update(s => s.DndUntil = DateTime.Now.AddMinutes(30));
            RefreshDndStatus();
            ToastHelper.SendSuccess("SysSuite", "Non disturbare: 30 minuti.");
        }

        private void BtnDnd1h_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Update(s => s.DndUntil = DateTime.Now.AddHours(1));
            RefreshDndStatus();
            ToastHelper.SendSuccess("SysSuite", "Non disturbare: 1 ora.");
        }

        private void BtnDnd4h_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Update(s => s.DndUntil = DateTime.Now.AddHours(4));
            RefreshDndStatus();
            ToastHelper.SendSuccess("SysSuite", "Non disturbare: 4 ore.");
        }

        private void BtnDndOff_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Update(s => s.DndUntil = null);
            RefreshDndStatus();
            ToastHelper.SendSuccess("SysSuite", "Non disturbare disattivato.");
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsService.SettingsFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
            }
            catch { }
        }

        private async void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ContentDialog
            {
                Title = "Cancellare la cronologia?",
                Content = "Verranno rimossi data ultimo boost, MB liberati e data ultima scansione salvati in locale.",
                PrimaryButtonText = "Cancella",
                CloseButtonText = "Annulla",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary)
                return;

            SettingsService.Update(s =>
            {
                s.LastBoostDate = null;
                s.LastBoostMB = 0;
                s.LastScanDate = null;
            });
            ToastHelper.SendSuccess("SysSuite", "Cronologia locale cancellata.");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Load();
            s.RestoreLastTabOnStartup = TogRestoreLastTab.IsOn;
            s.NotifyDiskFull = TogNotifyDisk.IsOn;
            s.NotifyHighCPU = TogNotifyCpu.IsOn;
            s.NotifyHighRAM = TogNotifyRam.IsOn;
            s.NotifyDriverProblems = TogNotifyDriver.IsOn;
            s.DiskWarningPct = SliderDisk.Value;
            s.CpuWarningPct = SliderCpu.Value;
            s.RamWarningPct = SliderRam.Value;
            s.BoostCleanTemp = TogBoostTemp.IsOn;
            s.BoostCleanThumbnails = TogBoostThumb.IsOn;
            s.BoostCleanBrowserCache = TogBoostBrowser.IsOn;
            s.BoostKillNotResponding = TogBoostKill.IsOn;
            SettingsService.Save(s);

            MainWindow.Instance?.ResetProactiveNotificationState();
            ToastHelper.SendSuccess("SysSuite One — Impostazioni", "Modifiche salvate.");
        }
    }
}
