using System;
using System.ComponentModel;
using Acornima.Ast;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using DirectPackageInstaller.Tasks;
using DirectPackageInstaller.ViewModels;

namespace DirectPackageInstaller.Views
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance;
        public MainWindow()
        {
            Instance = this;

            InitializeComponent();

            View = this.Find<MainView>("View");
            View.DataContext = new MainViewModel();

            Settings.CloseRequested += HideSettings;
            Settings.RestartServerRequested += () => View.RestartServer();
            Settings.DhcpToggleRequested += () => View.ToggleDhcpService();

            //Arquivos soltos fora da MainView (barra de título e bordas) também são aceitos
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, (_, e) => View.HandleDragOver(e));
            AddHandler(DragDrop.DropEvent, (_, e) => View.HandleDrop(e));

            Opened += MainWindowOpened;
            Closing += MainWindowClosing;
        }

        private async void MainWindowOpened(object? sender, EventArgs e)
        {
#if DEBUG
            this.AttachDevTools();
#endif
            await View.OnShown(this);
        }

        // ---- Barra de título e moldura próprias ----

        private const string MaximizeGeometry = "M3.5,3.5 H12.5 V12.5 H3.5 Z";
        private const string RestoreGeometry = "M5.5,3.5 H12.5 V10.5 M3.5,5.5 H10.5 V12.5 H3.5 Z";

        private void TitleBarPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                BeginMoveDrag(e);
        }

        private void ToggleMaximize()
        {
            if (!CanResize)
                return;

            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void SettingsClick(object? sender, RoutedEventArgs e)
        {
            Settings.DataContext = View.DataContext;
            SettingsOverlay.IsVisible = true;
        }

        private void HideSettings() => SettingsOverlay.IsVisible = false;

        // Clique fora do diálogo fecha; clique dentro não propaga ao fundo
        private void SettingsBackdropPressed(object? sender, PointerPressedEventArgs e) => HideSettings();

        private void SettingsContentPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

        private void RestartClick(object? sender, RoutedEventArgs e) => View.RestartServer();

        private void MinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

        private void CloseClick(object? sender, RoutedEventArgs e) => Close();

        private void ResizeGripPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!CanResize || WindowState != WindowState.Normal)
                return;

            if (sender is Control { Tag: string Name } && Enum.TryParse(Name, out WindowEdge Edge))
                BeginResizeDrag(Edge, e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property != WindowStateProperty)
                return;

            var Maximized = WindowState == WindowState.Maximized;

            var Glyph = this.Find<Avalonia.Controls.Shapes.Path>("MaximizeGlyph");
            if (Glyph != null)
                Glyph.Data = Geometry.Parse(Maximized ? RestoreGeometry : MaximizeGeometry);

            var Frame = this.Find<Border>("WindowFrame");
            if (Frame != null)
                Frame.BorderThickness = new Thickness(Maximized ? 0 : 1);

            var Grips = this.Find<Grid>("ResizeGrips");
            if (Grips != null)
                Grips.IsVisible = !Maximized;
        }

        private bool ForceClose = false;

        private async void MainWindowClosing(object? sender, CancelEventArgs e)
        {
            try
            {
                bool PS4Connected = Installer.Server?.Connections > 0;
                PS4Connected |= (DateTime.Now - Installer.Server?.LastRequest)?.TotalSeconds < 5;

                if (PS4Connected && !ForceClose)
                {
                    App.Callback(async () =>
                    {
                        if (await MessageBox.ShowAsync("The playstation is still downloading\nDo you really wanna exit?", "DirectPackageInstaller", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                            return;

                        ForceClose = true;
                        Close();
                    });
                    e.Cancel = true;
                    return;
                }
            }
            catch { }

            View.Status.Text = "Shutting Down Servers...";

            App.SaveSettings();
            await Installer.Payload.StopServer();

            if (View.DHCP != null)
            {
                View.DHCP.Stop();
                await App.SetupDHCPNetwork();
            }
        }
    }
}