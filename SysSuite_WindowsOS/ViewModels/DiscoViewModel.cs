using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    public partial class DiscoViewModel : ObservableObject
    {
        private readonly DiskAnalyzer _analyzer;
        private readonly CleanupService _cleanup;
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DiscoViewModel must be created on the UI thread.");

        private CancellationTokenSource? _scanCts;

        /// <summary>Testo di stato su una riga: evita stringhe enormi in UI.</summary>
        private static string TruncateStatus(string text, int maxLen) =>
            text.Length <= maxLen ? text : text[..maxLen] + "...";

        public DiscoViewModel(DiskAnalyzer analyzer, CleanupService cleanup)
        {
            _analyzer = analyzer;
            _cleanup = cleanup;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BusyRingVisibility))]
        private bool _isBusy;

        [ObservableProperty] private string _scanStatusText = "";

        [ObservableProperty] private string _scanTypeTitle = "Seleziona una scansione per iniziare.";

        [ObservableProperty]
        private ObservableCollection<DiscoItemRow> _listRows = new();

        [ObservableProperty] private DiscoItemRow? _selectedRow;

        public Visibility BusyRingVisibility =>
            IsBusy ? Visibility.Visible : Visibility.Collapsed;

        partial void OnIsBusyChanged(bool value)
        {
            ScanHeavyFoldersCommand.NotifyCanExecuteChanged();
            ScanLargeFilesCommand.NotifyCanExecuteChanged();
            ScanSmartCommand.NotifyCanExecuteChanged();
            ScanDuplicatesCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedRowChanged(DiscoItemRow? value) =>
            OpenExplorerForSelectionCommand.NotifyCanExecuteChanged();

        private void CancelScan()
        {
            try { _scanCts?.Cancel(); }
            catch { /* ignore */ }
            _scanCts?.Dispose();
            _scanCts = null;
        }

        private bool CanStartScan() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanStartScan))]
        private async Task ScanHeavyFoldersAsync()
        {
            CancelScan();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;
            IsBusy = true;
            ListRows.Clear();
            ScanTypeTitle = "CARTELLE PESANTI — C:";
            ScanStatusText = "Analisi in corso...";
            try
            {
                await Task.Run(async () =>
                {
                    var progress = new Progress<string>(p =>
                        _dispatcher.TryEnqueue(() => ScanStatusText = TruncateStatus(p, 40)));

                    await foreach (var d in _analyzer.GetHeavyFoldersAsync(@"C:\", 2, progress, ct)
                        .ConfigureAwait(false))
                    {
                        var row = new DiscoItemRow
                        {
                            Size = d.SizeFormatted,
                            Path = d.Path,
                            SizeBytes = d.SizeBytes
                        };
                        _dispatcher.TryEnqueue(() => ListRows.Add(row));
                    }

                    _dispatcher.TryEnqueue(() =>
                    {
                        var top = ListRows.OrderByDescending(r => r.SizeBytes).Take(100).ToList();
                        ListRows.Clear();
                        foreach (var r in top)
                            ListRows.Add(r);
                        ScanStatusText = ListRows.Count + " cartelle";
                    });
                }, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                _dispatcher.TryEnqueue(() => ScanStatusText = "Annullato.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore scansione disco");
                _dispatcher.TryEnqueue(() => ScanStatusText = "Errore: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartScan))]
        private async Task ScanLargeFilesAsync()
        {
            CancelScan();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;
            IsBusy = true;
            ListRows.Clear();
            ScanTypeTitle = "FILE GRANDI (> 100 MB) — C:";
            ScanStatusText = "Ricerca file grandi...";
            try
            {
                await Task.Run(async () =>
                {
                    var progress = new Progress<string>(p =>
                        _dispatcher.TryEnqueue(() => ScanStatusText = TruncateStatus(p, 40)));

                    await foreach (var d in _analyzer.GetLargeFilesAsync(@"C:\", 100L * 1024 * 1024, progress, ct)
                        .ConfigureAwait(false))
                    {
                        string full = Path.Combine(d.Path, d.Name);
                        var row = new DiscoItemRow
                        {
                            Size = d.SizeFormatted,
                            Path = full,
                            SizeBytes = d.SizeBytes
                        };
                        _dispatcher.TryEnqueue(() => ListRows.Add(row));
                    }

                    _dispatcher.TryEnqueue(() =>
                    {
                        var top = ListRows.OrderByDescending(r => r.SizeBytes).Take(200).ToList();
                        ListRows.Clear();
                        foreach (var r in top)
                            ListRows.Add(r);
                        ScanStatusText = ListRows.Count + " file";
                    });
                }, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                _dispatcher.TryEnqueue(() => ScanStatusText = "Annullato.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore scansione disco");
                _dispatcher.TryEnqueue(() => ScanStatusText = "Errore: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartScan))]
        private async Task ScanSmartAsync()
        {
            CancelScan();
            IsBusy = true;
            ScanTypeTitle = "DATI S.M.A.R.T. — TUTTI I DISCHI";
            ScanStatusText = "Lettura SMART...";
            ListRows.Clear();
            try
            {
                var result = await _analyzer.GetSmartDataAsync().ConfigureAwait(true);
                foreach (var d in result)
                {
                    ListRows.Add(new DiscoItemRow
                    {
                        Size = d.FailPredicted ? "GUASTO" : "OK",
                        Path = d.DiskModel + "  S/N:" + d.SerialNumber + "  " + d.Interface + "  "
                            + Math.Round(d.SizeBytes / 1_073_741_824.0, 1) + " GB",
                        SizeBytes = 0
                    });
                }

                ScanStatusText = result.Count + " dischi";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore scansione disco");
                ScanStatusText = "Errore: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartScan))]
        private async Task ScanDuplicatesAsync()
        {
            CancelScan();
            IsBusy = true;
            ScanTypeTitle = "FILE DUPLICATI — C:\\Users";
            ScanStatusText = "Ricerca duplicati (può richiedere alcuni minuti)...";
            ListRows.Clear();
            try
            {
                var progress = new Progress<string>(p =>
                    _dispatcher.TryEnqueue(() => ScanStatusText = TruncateStatus(p, 50)));
                var result = await _cleanup.FindDuplicatesAsync(@"C:\Users", progress).ConfigureAwait(true);
                long totalWasted = result.Sum(g => g.WastedBytes);
                foreach (var g in result)
                {
                    ListRows.Add(new DiscoItemRow
                    {
                        Size = DiskEntry.FormatSize(g.WastedBytes) + " sprecati",
                        Path = g.FileName + "  (" + g.Files.Count + " copie)",
                        SizeBytes = g.WastedBytes
                    });
                }

                ScanStatusText = result.Count + " gruppi — " + DiskEntry.FormatSize(totalWasted) + " recuperabili";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore scansione disco");
                ScanStatusText = "Errore: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanOpenExplorerForSelection() =>
            SelectedRow is { Path: var path } && !string.IsNullOrEmpty(path);

        [RelayCommand(CanExecute = nameof(CanOpenExplorerForSelection))]
        private void OpenExplorerForSelection()
        {
            if (SelectedRow is not { Path: var path } || string.IsNullOrEmpty(path))
                return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Errore apertura Explorer");
            }
        }
    }
}
