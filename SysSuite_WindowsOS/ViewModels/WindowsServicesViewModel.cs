using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SysSuite.Models;
using SysSuite.Services;

namespace SysSuite.ViewModels
{
    /// <summary>Riceve l'elenco servizi da <see cref="ServicesManager.EnumerateAllServicesNative"/> (modelli leggeri).</summary>
    public sealed partial class WindowsServicesViewModel : ObservableObject
    {
        public ObservableCollection<WindowsServiceListItem> Services { get; } = new();

        [ObservableProperty] private bool _isLoading;

        public async Task RefreshAsync(ServicesManager manager, CancellationToken cancellationToken = default)
        {
            IsLoading = true;
            try
            {
                IReadOnlyList<WindowsServiceListItem> list = await Task.Run(
                    manager.EnumerateAllServicesNative,
                    cancellationToken).ConfigureAwait(true);

                Services.Clear();
                foreach (WindowsServiceListItem row in list)
                    Services.Add(row);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
