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
        private double _ramMB;
        private int _threads;
        private int _handles;
        private bool _responding;
        private string _path = "";
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
            set
            {
                // Epsilon più ampio: evita PropertyChanged ogni tick su jitter sotto‑decimale (lista processi).
                if (Math.Abs(_cpuPercent - value) < 0.02) return;
                _cpuPercent = value;
                OnPropertyChanged();
                RefreshCpuBindings();
            }
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

        public string Path
        {
            get => _path;
            set { if (_path == value) return; _path = value; OnPropertyChanged(); }
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

        /// <summary>Primo inserimento in cache/lista: copia completa dal campione.</summary>
        public void InitializeFromSample(ProcessEntry sample)
        {
            PID = sample.PID;
            CopyMetricsFrom(sample);
        }

        /// <summary>Aggiornamento differenziale: solo CPU, RAM e stato (in esecuzione / non risponde).</summary>
        public void ApplyDynamicMetricsFrom(ProcessEntry sample)
        {
            if (sample.PID != PID) return;
            CpuPercent = sample.CpuPercent;
            RamMB = sample.RamMB;
            Responding = sample.Responding;
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
    }
}
