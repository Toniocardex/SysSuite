using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SysSuite;
using SysSuite.ViewModels;

namespace SysSuite.Views
{
    public sealed partial class PrivacyPage : UserControl
    {
        /// <summary>Inoltra messaggi di log verso la finestra host (es. MainSuitePage).</summary>
        public event Action<string, string>? Log;

        public PrivacyViewModel ViewModel { get; }

        public PrivacyPage()
        {
            InitializeComponent();
            ViewModel = App.Services.GetRequiredService<PrivacyViewModel>();
            DataContext = ViewModel;
            ViewModel.DiagnosticLog += (msg, type) => Log?.Invoke(msg, type);
            Loaded += (_, _) => ViewModel.LoadFromRegistry();
        }
    }
}
