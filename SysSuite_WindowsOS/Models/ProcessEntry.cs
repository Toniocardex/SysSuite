using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SysSuite.Models
{
    public class ProcessEntry : INotifyPropertyChanged
    {
        private static readonly SolidColorBrush CpuHighBrush =
            new(Color.FromArgb(255, 255, 90, 90));
        private static readonly SolidColorBrush CpuMidBrush =
            new(Color.FromArgb(255, 255, 181, 71));
        private static readonly SolidColorBrush CpuLowBrush =
            new(Color.FromArgb(255, 61, 77, 102));
        private static readonly SolidColorBrush RespOkBrush =
            new(Color.FromArgb(255, 52, 211, 153));
        private static readonly SolidColorBrush RespBadBrush =
            new(Color.FromArgb(255, 255, 90, 90));

        private int _pid;
        private string _name = "";
        private double _cpuPercent;
        private double _lastNotifiedCpuPercent = double.NaN;
        private double _ramMB;
        private int _threads;
        private int _handles;
        private bool _responding;
        private string _imagePath = "";
        private string _description = "";
        private DateTime _startTime;

        public int PID
        {
            get => _pid;
            set { if (_pid == value) return; _pid = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnPropertyChanged(); }
        }

        public double CpuPercent
        {
            get => _cpuPercent;
            set => SetCpuPercentValue(value, forceNotify: false);
        }

        public double RamMB
        {
            get => _ramMB;
            set
            {
                if (Math.Abs(_ramMB - value) < 0.01) return;
                _ramMB = value;
                OnPropertyChanged();
            }
        }

        public int Threads
        {
            get => _threads;
            set { if (_threads == value) return; _threads = value; OnPropertyChanged(); }
        }

        public int Handles
        {
            get => _handles;
            set { if (_handles == value) return; _handles = value; OnPropertyChanged(); }
        }

        public bool Responding
        {
            get => _responding;
            set
            {
                if (_responding == value) return;
                _responding = value;
                OnPropertyChanged();
                RefreshRespondingBindings();
            }
        }

        /// <summary>Percorso immagine eseguibile (solo <see cref="InitializeFromSample"/> sulla riga in cache).</summary>
        public string ImagePath => _imagePath;

        /// <summary>Alias di <see cref="ImagePath"/> per campioni da ProcessManager.</summary>
        public string Path
        {
            get => _imagePath;
            set
            {
                if (_imagePath == value) return;
                _imagePath = value ?? "";
                OnPropertyChanged(nameof(Path));
                OnPropertyChanged(nameof(ImagePath));
            }
        }

        public string Description
        {
            get => _description;
            set { if (_description == value) return; _description = value; OnPropertyChanged(); }
        }

        public DateTime StartTime
        {
            get => _startTime;
            set { if (_startTime == value) return; _startTime = value; OnPropertyChanged(); }
        }

        /// <summary>Testo CPU per binding (si aggiorna con CpuPercent).</summary>
        public string CpuStr => CpuPercent > 0 ? CpuPercent.ToString("0.0") + "%" : "—";

        public SolidColorBrush CpuForeground =>
            CpuPercent >= 30 ? CpuHighBrush
            : CpuPercent >= 5 ? CpuMidBrush
            : CpuLowBrush;

        public string RespondingText => Responding ? "OK" : "Non risponde";

        public SolidColorBrush RespondingForeground =>
            Responding ? RespOkBrush : RespBadBrush;

        /// <summary>Aggiorna metriche da un campione fresco (stesso PID, altra istanza).</summary>
        public void CopyMetricsFrom(ProcessEntry src)
        {
            if (src.PID != PID) return;
            Name = src.Name;
            CpuPercent = src.CpuPercent;
            RamMB = src.RamMB;
            Threads = src.Threads;
            Handles = src.Handles;
            Responding = src.Responding;
            Path = src.Path;
            Description = src.Description;
            StartTime = src.StartTime;
        }

        /// <summary>
        /// Prima associazione riga in cache: imposta <see cref="Name"/> e <see cref="ImagePath"/> (da <c>sample.Path</c>)
        /// e le altre proprietà statiche; notifica CPU con baseline per throttling 0.2%.
        /// </summary>
        public void InitializeFromSample(ProcessEntry sample)
        {
            _pid = sample.PID;
            _name = sample.Name ?? "";
            _imagePath = sample.Path ?? "";
            _description = sample.Description ?? "";
            _startTime = sample.StartTime;
            _threads = sample.Threads;
            _handles = sample.Handles;
            _responding = sample.Responding;
            _ramMB = sample.RamMB;
            _cpuPercent = sample.CpuPercent;
            _lastNotifiedCpuPercent = sample.CpuPercent;

            OnPropertyChanged(nameof(PID));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(ImagePath));
            OnPropertyChanged(nameof(Path));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(StartTime));
            OnPropertyChanged(nameof(Threads));
            OnPropertyChanged(nameof(Handles));
            OnPropertyChanged(nameof(Responding));
            OnPropertyChanged(nameof(RamMB));
            OnPropertyChanged(nameof(CpuPercent));
            RefreshCpuBindings();
            RefreshRespondingBindings();
        }

        /// <summary>Solo CPU, RAM e stato; non modifica <see cref="Name"/> né <see cref="ImagePath"/>.</summary>
        public void ApplyDynamicMetricsFrom(ProcessEntry sample)
        {
            if (sample.PID != PID) return;
            SetCpuPercentValue(sample.CpuPercent, forceNotify: false);
            RamMB = sample.RamMB;
            Responding = sample.Responding;
        }

        private void SetCpuPercentValue(double value, bool forceNotify)
        {
            if (Math.Abs(_cpuPercent - value) < 1e-9)
                return;

            _cpuPercent = value;

            bool significant = forceNotify
                || double.IsNaN(_lastNotifiedCpuPercent)
                || Math.Abs(value - _lastNotifiedCpuPercent) >= 0.2;

            if (!significant)
                return;

            _lastNotifiedCpuPercent = value;
            OnPropertyChanged(nameof(CpuPercent));
            RefreshCpuBindings();
        }

        private void RefreshCpuBindings()
        {
            OnPropertyChanged(nameof(CpuStr));
            OnPropertyChanged(nameof(CpuForeground));
        }

        private void RefreshRespondingBindings()
        {
            OnPropertyChanged(nameof(RespondingText));
            OnPropertyChanged(nameof(RespondingForeground));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class StartupEntry
    {
        public string Name     { get; set; } = "";
        public string Command  { get; set; } = "";
        public string Source   { get; set; } = "";
        public string RegPath  { get; set; } = "";
        public bool   Enabled  { get; set; } = true;
    }

    public class InstalledApp
    {
        public string Name        { get; set; } = "";
        public string Version     { get; set; } = "";
        public string Publisher   { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public long   SizeBytes   { get; set; }
        public string UninstallString { get; set; } = "";
        /// <summary>Valore registro DisplayIcon (es. percorso .ico o .exe,indice).</summary>
        public string DisplayIcon { get; set; } = "";
        public int    DaysUnused  { get; set; }
        public string RegistryKeyName { get; set; } = "";
    }
}
