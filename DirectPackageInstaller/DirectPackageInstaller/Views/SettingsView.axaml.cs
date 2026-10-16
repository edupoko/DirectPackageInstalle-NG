using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DirectPackageInstaller.Views
{
    /// <summary>
    /// Diálogo de configurações. Liga-se ao mesmo MainViewModel do menu Options,
    /// então as alterações passam pelo ModelOnPropertyChanged da MainView.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public event Action? CloseRequested;
        public event Action? RestartServerRequested;
        public event Action? DhcpToggleRequested;
        public event Action? BrowseLibraryRequested;

        public SettingsView()
        {
            InitializeComponent();

            cbDhcp.IsVisible = App.IsWindows;

            btnCloseSettings.Click += (_, _) => CloseRequested?.Invoke();
            btnDoneSettings.Click += (_, _) => CloseRequested?.Invoke();
            btnRestartServer.Click += (_, _) => RestartServerRequested?.Invoke();
            btnBrowseLibrary.Click += (_, _) => BrowseLibraryRequested?.Invoke();

            // O DHCP pede confirmação e inicia o serviço; a MainView alterna a propriedade.
            cbDhcp.Click += (_, _) => DhcpToggleRequested?.Invoke();
        }
    }
}
