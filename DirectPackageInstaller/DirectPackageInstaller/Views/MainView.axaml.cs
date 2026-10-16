using System;
using Avalonia.Platform.Storage;
using System.Threading;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LibOrbisPkg.PKG;
using SharpCompress.Archives;
using DirectPackageInstaller.Compression;
using DirectPackageInstaller.Host;
using DirectPackageInstaller.IO;
using DirectPackageInstaller.Others;
using DirectPackageInstaller.Tasks;
using DirectPackageInstaller.ViewModels;
using Image = Avalonia.Controls.Image;
using Path = System.IO.Path;
using Size = Avalonia.Size;
using LibOrbisPkg.GP4;
using Acornima.Ast;

namespace DirectPackageInstaller.Views
{
    public partial class MainView : UserControl
    {

        public DHCPHost? DHCP = null;
        public INetworkManagement? AdapterHelper = null;

        private Stream? PKGStream;
        
        private PkgReader PKGParser;
        private Pkg PKG;

        private bool BadHostAlert;
        
        private Source InputType = Source.NONE;

        private string? LastForcedSource = null;

        private List<(string Source, string Name)> BatchList = new List<(string Source, string Name)>();

        private MainWindow Parent;

        private IArchive? CurrentDecompressor = null;

        private DecompressorHelperStream[]? CurrentDecompressorVolumes = null;


        public MainViewModel? Model => (MainViewModel?)DataContext;
        
        public MainView()
        {
            InitializeComponent();

            MainViewGrid = this.Find<Grid>("MainViewGrid");

            PackagesMenu = this.Find<MenuItem>("PackagesMenu");

            tbURL = this.Find<TextBox>("tbURL");

            PreviewGrid = this.Find<Grid>("PreviewGrid");
            
            IconBox = this.Find<Image>("IconBox");
            MainGridSplitter = this.Find<GridSplitter>("MainGridSplitter");
            PkgInfoGrid = this.Find<DataGrid>("PkgInfoGrid");
            
            btnInstallAll = this.Find<MenuItem>("btnInstallAll");
            btnProxyDownload = this.Find<MenuItem>("btnProxyDownload");
            btnRestartServer = this.Find<MenuItem>("btnRestartServer");
            btnAllDebirdEnabled = this.Find<MenuItem>("btnAllDebirdEnabled");
            btnRealDebirdEnabled = this.Find<MenuItem>("btnRealDebirdEnabled");
            btnDebirdLinkEnabled = this.Find<MenuItem>("btnDebirdLinkEnabled");
            btnSegmentedDownload = this.Find<MenuItem>("btnSegmentedDownload");
            btnTransferProgress = this.Find<MenuItem>("btnTransferProgress");
            btnDHCPService = this.Find<MenuItem>("btnDHCPService");
            btnExit = this.Find<MenuItem>("btnExit");
            btnLoad = this.Find<Button>("btnLoad");
            
            btnInstallAll.Click += BtnInstallAllOnClick;
            btnRestartServer.Click += RestartServer_OnClick;
            btnProxyDownload.Click += BtnProxyDownloadOnClick;
            btnAllDebirdEnabled.Click += BtnAllDebirdEnabledOnClick;
            btnRealDebirdEnabled.Click += BtnRealDebirdEnabledOnClick;
            btnDebirdLinkEnabled.Click += BtnDebirdLinkEnabledOnClick;
            btnSegmentedDownload.Click += BtnSegmentedDownloadOnClick;
            btnTransferProgress.Click += BtnTransferProgressOnClick;
            btnDHCPService.Click += BtnDHCPServiceOnClick;

            btnLoad.Click += BtnLoadOnClick;

            tbURL.GotFocus += tbURLOnGotFocus;

            btnExit.Click += btnExitOnClick;
            btnExit.IsVisible = App.IsAndroid;
            
            PS4Server.GlobalTransferProgressChanged += ServerOnTransferProgressChanged;
            
            PreviewGrid.PropertyChanged += PreviewGridOnPropertyChanged;


            btnDHCPService.IsVisible = App.IsWindows;

            //No desktop, as opções ficam no diálogo de configurações da janela e o menu
            //só aparece quando há pacotes em lote (menu Packages), sem deixar uma faixa vazia.
            OptionsMenu.IsVisible = App.IsSingleView;
            UpdateMenuVisibility();
            PackagesMenu.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                    UpdateMenuVisibility();
            };
        }

        private void UpdateMenuVisibility() => MainMenu.IsVisible = App.IsSingleView || PackagesMenu.IsVisible;

        // ---- Console: detecção na rede e estado da conexão ----

        /// <summary>Pedido do indicador de console para abrir as configurações.</summary>
        public event Action? SettingsRequested;

        private bool ConsoleCheckRunning;

        //O servidor do GoldHEN (9090) é simples: consulta no máximo uma vez por minuto
        private static readonly TimeSpan GoldHenCheckInterval = TimeSpan.FromSeconds(60);
        private DateTime LastGoldHenCheck = DateTime.MinValue;
        private bool LastGoldHenOnline;
        private string? LastGoldHenIp;

        private void ConsoleStatusClick(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

        /// <summary>Verifica o console configurado (GoldHEN, RPI, etaHEN) e atualiza o indicador.</summary>
        public async Task RefreshConsoleStatus()
        {
            if (Model == null || ConsoleCheckRunning)
                return;

            ConsoleCheckRunning = true;

            var Ip = Model.PS4IP;

            try
            {
                Model.BeginConsoleCheck();

                var CheckGoldHen = Ip != LastGoldHenIp || DateTime.Now - LastGoldHenCheck >= GoldHenCheckInterval;

                var Result = await ConsoleStatusChecker.CheckAsync(Ip, CheckGoldHen, LastGoldHenOnline);

                if (CheckGoldHen)
                {
                    LastGoldHenCheck = DateTime.Now;
                    LastGoldHenIp = Ip;
                    LastGoldHenOnline = Result.GoldHen;
                }

                //O IP pode ter mudado durante a verificação; o próximo ciclo cuida do novo
                if (Model.PS4IP == Ip)
                    Model.ApplyConsoleStatus(Result);
            }
            catch
            {
            }
            finally
            {
                ConsoleCheckRunning = false;

                if (Model.PS4IP != Ip)
                    _ = RefreshConsoleStatus();
            }
        }

        /// <summary>Procura consoles PS4/PS5 na rede local e lista os resultados nas configurações.</summary>
        public async Task DetectConsoles()
        {
            if (Model == null || Model.IsScanningConsoles)
                return;

            Model.IsScanningConsoles = true;

            try
            {
                var Found = await ConsoleScanner.ScanAsync(TimeSpan.FromSeconds(6));

                Model.SetDetectedConsoles(Found);

                if (Found.Count == 0)
                {
                    Toast.Show("No console found", "Make sure it is on and connected to the same network, then try again.", ToastKind.Warning, 8);
                }
                else if (Found.Count == 1 && string.IsNullOrWhiteSpace(Model.PS4IP))
                {
                    UseConsole(Found[0]);
                }
                else
                {
                    Toast.Show(Found.Count == 1 ? "Console found" : $"{Found.Count} consoles found", "Pick one in Settings → Network.", ToastKind.Success);
                }
            }
            catch
            {
                Toast.Show("Network scan failed", "Check your network connection and try again.", ToastKind.Error);
            }
            finally
            {
                Model.IsScanningConsoles = false;
            }
        }

        /// <summary>Define o console escolhido como destino dos envios.</summary>
        public void UseConsole(DiscoveredConsole Found)
        {
            if (Model == null)
                return;

            Model.ConsoleType = Found.Type;
            Model.PS4IP = Found.Ip;

            //O IP do PC vem da interface em que o console respondeu
            if (!string.IsNullOrWhiteSpace(Found.LocalIp) && Found.LocalIp != "0.0.0.0")
                Model.PCIP = Found.LocalIp;

            RestartServer_OnClick(null, null);
            App.SaveSettings();

            Toast.Show("Console selected", $"{Found.Title}  ·  {Found.Ip}", ToastKind.Success);
        }

        /// <summary>Reinicia o servidor (usado pelo diálogo de configurações e pela barra de título).</summary>
        public void RestartServer() => RestartServer_OnClick(null, null);

        /// <summary>Liga/desliga o serviço DHCP, com a mesma confirmação do menu.</summary>
        public void ToggleDhcpService() => BtnDHCPServiceOnClick(null, new RoutedEventArgs());
        // ---- Biblioteca ----

        private CancellationTokenSource? LibraryCts;

        /// <summary>Lê novamente a pasta da biblioteca em segundo plano.</summary>
        public async Task RefreshLibrary()
        {
            if (Model == null)
                return;

            LibraryCts?.Cancel();
            var Cts = LibraryCts = new CancellationTokenSource();

            Model.ClearLibrary();

            var Root = Model.LibraryPath;

            if (string.IsNullOrWhiteSpace(Root) || !Directory.Exists(Root))
            {
                Model.FinishLibraryScan();
                return;
            }

            Model.IsScanningLibrary = true;

            try
            {
                await Task.Run(() => LibraryScanner.Scan(Root, Item =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!Cts.IsCancellationRequested)
                            Model.AddLibraryItem(Item);
                    });
                }, Cts.Token));
            }
            catch
            {
            }
            finally
            {
                if (!Cts.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => Model.FinishLibraryScan());
            }
        }

        /// <summary>Abre o seletor de pastas e define a pasta da biblioteca.</summary>
        public async Task ChooseLibraryFolder()
        {
            if (Model == null)
                return;

            var Provider = TopLevel.GetTopLevel(this)?.StorageProvider;

            if (Provider is not { CanPickFolder: true })
            {
                await MessageBox.ShowAsync("Folder selection isn't available on this platform.\nType the folder path in the Settings instead.", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var Result = await Provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the folder with your PKG files",
                AllowMultiple = false
            });

            var Folder = Result.FirstOrDefault()?.TryGetLocalPath();

            if (string.IsNullOrWhiteSpace(Folder))
                return;

            Model.LibraryPath = Folder;
            Model.ShowLibrary = true;
        }

        private void TabLibraryClick(object? sender, RoutedEventArgs e)
        {
            if (Model != null)
                Model.ShowLibrary = true;
        }

        private void TabPackageClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;

            Model.ShowLibrary = false;
            Model.ShowHistory = false;
        }

        private void LibraryChooseClick(object? sender, RoutedEventArgs e) => _ = ChooseLibraryFolder();

        private void LibraryRefreshClick(object? sender, RoutedEventArgs e) => _ = RefreshLibrary();

        private void LibraryItemClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null || (sender as Control)?.DataContext is not LibraryItem Item)
                return;

            Model.ShowLibrary = false;
            Model.ShowHistory = false;

            //Já carregado: só mostra os detalhes (carregar de novo acionaria a instalação)
            if (Model.CurrentURL == Item.Path && InputType != Source.NONE)
                return;

            if (Model.CurrentURL != Item.Path)
            {
                BatchList.Clear();
                Model.CurrentURL = Item.Path;
            }

            App.Callback(() => BtnLoadOnClick(null, new RoutedEventArgs()));
        }

        public async Task OnShown(MainWindow? Parent)
        {
            if (Model == null)
                return;

            if (Parent != null)
            {
                this.Parent = Parent;

                this.Parent.Title += $" - v{SelfUpdate.CurrentVersion}";
            }

            if (File.Exists(App.SettingsPath))
            {
                App.Config = new Settings();
                var IniReader = new Ini(App.SettingsPath, "Settings");

                App.Config.EthernetAdapter = IniReader.GetValue("EthernetAdapter");
                App.Config.EnableDHCP = IniReader.GetBooleanValue("EnableDHCP");

                App.Config.PSIP = IniReader.GetValue("PS4IP");
                App.Config.PCIP = IniReader.GetValue("PCIP");
                App.Config.SearchPS4 = IniReader.GetBooleanValue("SearchPS4");
                App.Config.ProxyDownload = IniReader.GetBooleanValue("ProxyDownload");
                App.Config.SegmentedDownload = IniReader.GetBooleanValue("SegmentedDownload");
                App.Config.UseAllDebrid = IniReader.GetBooleanValue("UseAllDebrid");
                App.Config.AllDebridApiKey = IniReader.GetValue("AllDebridApiKey");
                App.Config.UseRealDebrid = IniReader.GetBooleanValue("UseRealDebrid");
                App.Config.UseDebridLink = IniReader.GetBooleanValue("UseDebridLink");
                App.Config.RealDebridApiKey = IniReader.GetValue("RealDebridApiKey");
                App.Config.DebridLinkApiKey = IniReader.GetValue("DebridLinkApiKey");
                App.Config.ShowError = IniReader.GetBooleanValue("ShowError");
                var ShowTransferProgress = IniReader.GetValue("ShowTransferProgress");
                App.Config.ShowTransferProgress = string.IsNullOrWhiteSpace(ShowTransferProgress) || IniReader.GetBooleanValue("ShowTransferProgress");
                App.Config.SkipUpdateCheck = IniReader.GetBooleanValue("SkipUpdateCheck");
                App.Config.AutoSplitPKG = IniReader.GetBooleanValue("AutoSplitPKG");
                App.Config.LibraryPath = IniReader.GetValue("LibraryPath");

                App.Config.PayloadPort = IniReader.GetIntValue("PayloadPort");
                
                ConnectionHelper.AllowReconnect = IniReader.GetBooleanValue("AllowReconnect");

                var Concurrency = IniReader.GetValue("Concurrency");
                if (!string.IsNullOrWhiteSpace(Concurrency) && int.TryParse(Concurrency, out int ConcurrencyNum))
                    SegmentedStream.DefaultConcurrency = ConcurrencyNum;

                if (ConnectionHelper.AllowReconnect && !ConnectionHelper.IsWindowsAdministrator())
                {
                    await MessageBox.ShowAsync("Please, Restart the DirectPackageInstaller as Administrator\nto use the Auto Reconnection feature.", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                App.Config = new Settings()
                {
                    PCIP = "0.0.0.0",
                    PSIP = "",
                    SearchPS4 = true,
                    ProxyDownload = true,
                    SegmentedDownload = !App.IsAndroid,
                    UseAllDebrid = false,
                    ShowError = false,
                    ShowTransferProgress = true,
                    SkipUpdateCheck = false,
                    EnableDHCP = false,
                    AllDebridApiKey = null,
                    RealDebridApiKey = null
                };

                var DHCPHint = "With DirectPackageInstaller your PS4/PS5 and PC can do a \"Plug And Play\" network connection just enable the DHCP Server option and select your network adapter if needed.";
                var ProxyHint = "If your download speed is very slow, you can try enable the \"Proxy Downloads\" feature, since this feature has been created just to optimize the download speed.";
                var CompressedHint = "When downloading directly from compressed files, you can't resume the download after the DirectPackageInstaller is closed, but before close the DirectPackageInstaller you still can pause and resume the download in your PS4.";
                var AndroidHints = "Is recommended to keep your phone in charger to prevent any battery optimization problems.\nIf you're using MIUI, disable the battery optimizations manually in the app properties.";
                var FirewallHint = $"The DirectPackageInstaller use the port {Installer.ServerPort} in the \"Proxy Downloads\" feature, maybe you will need to open the ports in your firewall.";
                var ResumeHint = "Direct PKG urls, using the \"Proxy Download\" feature or not, can be resumed anytime by just selecting 'resume' in your PS4/PS5 download list.";
                var IndirectDownloadHint = "When using the \"Proxy Downloads\" feature, the PS4/PS5 can't download the game alone and the DirectPackageInstaller must keep open.";
                var DirectDownloadHint = "When using the direct download mode, you can turn off the computer or close the DirectPakcageInstaller and your PS4/PS5 will continue the download alone.";
                var WelcomeText = $"Welcome. User, The focus of this tool is download PKGs from direct links but we have others minor features as well.\n\nGood to know:\n{DirectDownloadHint}\n\n{IndirectDownloadHint}\n\n{ResumeHint}\n\n{(App.IsAndroid ? AndroidHints : FirewallHint)}\n\n{CompressedHint}\n\n{ProxyHint}\n\n{(App.IsWindows? DHCPHint  + "\n\n": "")}Created by marcussacana";
                
                await MessageBox.ShowAsync(WelcomeText, "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Model.DHCPService = App.Config.EnableDHCP;
            Model.ProxyMode = App.Config.ProxyDownload;
            Model.ShowTransferProgress = App.Config.ShowTransferProgress;
            Model.UseAllDebrid = App.Config.UseAllDebrid;
            Model.UseDebridLink = App.Config.UseDebridLink;
            Model.SegmentedMode = App.Config.SegmentedDownload;
            Model.UseRealDebrid = App.Config.UseRealDebrid;
            Model.PS4IP = App.Config.PSIP;
            Model.PCIP = App.Config.PCIP;
            Model.AllDebridApiKey = App.Config.AllDebridApiKey;
            Model.RealDebridApiKey = App.Config.RealDebridApiKey;
            Model.DebridLinkApiKey = App.Config.DebridLinkApiKey;
            Model.LibraryPath = App.Config.LibraryPath;
             
            if (App.Config.SearchPS4 || string.IsNullOrEmpty(App.Config.PSIP))
            {
                _ = PS4Finder.StartFinder(OnClientFound());
            }

            if (App.Config.EnableDHCP && !string.IsNullOrWhiteSpace(App.Config.EthernetAdapter))
            {
                await StartDHCP(App.Config.EthernetAdapter);
            }

            if (!string.IsNullOrEmpty(App.Config.PSIP))
                await Installer.StartServer(App.Config.PCIP);

             Model.SetHistory(HistoryStore.Load());
             TransferMonitor.Instance.SessionEnded += OnTransferSessionEnded;

             //Atualiza a barra de estado do envio (velocidade, conclusão, modo direto)
             var TransferTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
             TransferTimer.Tick += (_, _) =>
             {
                 TransferMonitor.Instance.Tick();
                 Model?.ApplyTransfer(TransferMonitor.Instance);
             };
             TransferTimer.Start();

             //Revalida o console a cada 10 s (ligado, instalador ativo, rede)
             var ConsoleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
             ConsoleTimer.Tick += (_, _) => _ = RefreshConsoleStatus();
             ConsoleTimer.Start();
             _ = RefreshConsoleStatus();

             Model.PropertyChanged += ModelOnPropertyChanged;

             //Com uma pasta configurada, abre direto na biblioteca
             Model.ShowLibrary = Model.HasLibraryPath;
             _ = RefreshLibrary();
             
             App.Callback(async () =>
             {
                 var MissingRuntime = await App.Updater.RequiresNewRuntime();
                 if (!App.Config.SkipUpdateCheck && MissingRuntime != null)
                 {
                     var Response = await MessageBox.ShowAsync($"A New update has been released, but you can't update because a required program is missing.\nNow, the DirectPackageInstaller will use .NET {MissingRuntime}, and you can update only after install the Runtime, Press Yes to Visit the Download Page.", "DirectPackageInstaller - MISSING RUNTIME", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                     if (Response == DialogResult.Yes)
                     { 
                         OpenUrl($"https://dotnet.microsoft.com/en-us/download/dotnet/{MissingRuntime}/runtime");
                     }
                     return;
                 }
                 
                 if (!App.Config.SkipUpdateCheck && await App.Updater.HasUpdates())
                 {
                     var Response = await MessageBox.ShowAsync($"New Update Found, You're using the {SelfUpdate.CurrentVersion} the last version is {SelfUpdate.LastVersion},\nDo you wanna update the DirectPackageInstaller now?", "DirectPackageInstaller", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                     if (Response != DialogResult.Yes)
                         return;
                     
                     await App.Updater.DownloadUpdate();
                 }
             });

             if (string.IsNullOrWhiteSpace(App.WorkingDirectory))
             {
                 await MessageBox.ShowAsync("Failed find the Working Directory Path of the DirectPackageInstaller.\nPlease, Restart the Program.", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 Environment.Exit(0);
             }
        }

        private Action<IPAddress, IPAddress?, int> OnClientFound()
        {
            return (PS4IP, PCIP, VERSION) =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (!string.IsNullOrEmpty(Model.PS4IP))
                    {
                        if (Model.PS4IP == PS4IP.ToString() || await MessageBox.ShowAsync($"A new PS{VERSION} IP has been found ({PS4IP}), Update the PS{VERSION} IP?", "DirectPackageInstaller", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                            return;
                    }
                    var FirstConsole = string.IsNullOrEmpty(Model.PS4IP);

                    Model.ConsoleType = $"PS{VERSION}";
                    Model.PS4IP = PS4IP.ToString();

                    if (FirstConsole)
                        Toast.Show("Console found", $"PS{VERSION}  ·  {PS4IP}", ToastKind.Success);

                    var NewPCIP = PCIP?.ToString() ?? IPHelper.FindLocalIP(PS4IP.ToString()) ?? "";
                    if (!string.IsNullOrWhiteSpace(NewPCIP) && NewPCIP != "0.0.0.0")
                        Model.PCIP = NewPCIP;

                    RestartServer_OnClick(null, null);

                    if (App.IsAndroid)
                        App.SaveSettings();
                });
            };
        }

        private async Task StartDHCP(string? DefaultAdapter = null)
        {
            if (DHCP?.Active ?? false) 
                return;

            if (AdapterHelper == null)
            {
                if (App.IsWindows)
                {
                    AdapterHelper = new WindowsNetworkManagement();
                }
                else
                {
                    if (Model != null)
                        Model!.DHCPService = false;

                    await MessageBox.ShowAsync("Currently, the DHCP Server is not supported in the current platform", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (DefaultAdapter == null) {

                await SetStatus("Analyzing Network Adapters...");

                var Adapters = await AdapterHelper.GetValidAdapters();

                if (Adapters.Count() == 1)
                {
                    AdapterHelper.Adapter = Adapters.Single();
                }
                else
                {
                    if (App.IsSingleView)
                    {
                        var ChoiceBox = Extensions.CreateInstance<SelectView>(new SelectViewModel());
                        ((SelectViewModel)ChoiceBox.DataContext).Caption = "Adapter:";
                        ChoiceBox.Initialize(null, Adapters, (n) =>
                        {
                            AdapterHelper.Adapter = n;
                            SingleView.ReturnView(ChoiceBox);
                        });

                        await SingleView.CallView(ChoiceBox, true);
                    }
                    else
                    {
                        var ChoiceBox = new Select(Adapters);
                        ChoiceBox.Caption = "Adapter:";
                        if (await ChoiceBox.ShowDialogAsync() == DialogResult.OK)
                            AdapterHelper.Adapter = ChoiceBox.Choice;
                    }
                }

            }
            else
            {
                AdapterHelper.Adapter = DefaultAdapter;
            }

            if (AdapterHelper.Adapter == null)
            {
                if (Model != null)
                    Model!.DHCPService = false;

                await MessageBox.ShowAsync("Failed to find the ethernet network adapter.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (DHCP == null)
            {
                DHCP = new DHCPHost();
                DHCP.OnNewClient += OnClientFound();
                DHCP.OnNewClient += async (a, b, c) => {
                    await SetStatus($"Device Connected, IP: {a}");
                };
            }

            App.Config.EthernetAdapter = AdapterHelper.Adapter;

            await SetStatus("Initializing DHCP...");

            if (!await App.SetupStaticNetwork())
            {
                if (Model != null)
                    Model!.DHCPService = false;

                await SetStatus("");

                await MessageBox.ShowAsync("Failed to setup the network adapter.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DHCP.StartInBackground();

            await SetStatus("");
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
        private async void BtnDHCPServiceOnClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;

            Model.DHCPService = !Model.DHCPService;

            if (!Model.DHCPService)
            {
                if (DHCP != null)
                {
                    await SetStatus("Restoring Network Settings...");

                    DHCP.Stop();
                    await App.SetupDHCPNetwork();

                    await SetStatus("");
                }

                DHCP?.Dispose();
                DHCP = null;

                App.Config.EthernetAdapter = "";
            }
            else
            {
                if (await MessageBox.ShowAsync("The DHCP Service will change your network settings, are you sure?\n\nNote: When you close the DirectPackageInstaller your network settings will be restored.", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    await StartDHCP();
                else
                    Model.DHCPService = false;
            }
        }

        private async void BtnLoadOnClick(object? sender, RoutedEventArgs e)
        {
            if (e != null)
            {
                App.Callback(() => BtnLoadOnClick(sender, null));
                return;
            }
            
            PKGStream?.Close();
            PKGStream?.Dispose();

            var ForcedSource = sender as string;

            var SourcePackage = Model?.CurrentURL;

            //Check if the BtnLoadOnClicked has manually called with an file path as source
            if (ForcedSource is {Length: > 2} && (ForcedSource[1] == ':' || ForcedSource[0] == '/'))
                SourcePackage = LastForcedSource = ForcedSource;
            
            //To hide the Packages menu if a different source is loaded
            if (BatchList.All(x => x.Source != SourcePackage))
                BatchList.Clear();

            if (string.IsNullOrWhiteSpace(SourcePackage) && !string.IsNullOrWhiteSpace(LastForcedSource))
                SourcePackage = LastForcedSource;

            //Already Loaded, then Let's Install it!
            if (InputType != Source.NONE) {
                var Success = await Install(SourcePackage, false);

                if (InputType.HasFlag(Source.File) && Success)
                    App.Callback(() => Model.CurrentURL = string.Empty);
                
                return;
            }

            //No Source, Open File Selection Dialog
            if (string.IsNullOrEmpty(SourcePackage))
            {

                string[]? Result;

                if (!App.IsSingleView)
                {
                    var FD = new OpenFileDialog
                    {
                        Filters = new List<FileDialogFilter>()
                        {
                            new()
                            {
                                Name = "ALL PKG Files",
                                Extensions = new List<string>() {"pkg", "PKG", "json", "JSON"},
                            },
                            new()
                            {
                                Name = "Split PKG Manifest",
                                Extensions = new List<string>() {"json", "JSON"}
                            },
                            new()
                            {
                                Name = "ALL Files",
                                Extensions = new List<string>() {"*"}
                            }
                        },
                        AllowMultiple = true
                    };

                    Result = await FD.ShowAsync(Parent);
                }
                else
                {
                    var Picker = new FilePicker();
                    await Picker.OpenDir(App.RootDir);
                    
                    await SingleView.CallView(Picker, false);
                    Result = Picker.SelectedFiles.ToArray();
                }

                
                BatchList.Clear();
                
                if (Result is {Length: > 0})
                {
                    
                    if (Result.Length > 1)
                        BatchList.AddRange(Result.Select(x => (x, Path.GetFileName(x))));
                    
                    ListEntries(Result);
                    
                    App.Callback(() => Model.CurrentURL = Result.First());
                }

                return;
            }


            PKGStream?.Close();

            bool LimitedFHost = false;

            PKG = null;
            PKGParser = null;
            PKGStream = null;

            Installer.EntryFileName = null;

            GC.Collect();
            
            //Sender is a stream during compressed file selection
            if (ForcedSource == null && sender is not Stream)
            {
                PackagesMenu.IsVisible = BatchList.Any();
                Installer.CurrentFileList = null;
            }

            InputType = Source.NONE;

            if (!SourcePackage.IsValidURL() && !File.Exists(SourcePackage)) {
                await MessageBox.ShowAsync(Parent, "Invalid URL or File Path", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                tbURL.IsEnabled = false;
                btnLoad.IsEnabled = false;
                await App.DoEvents();

                PKGStream = sender as Stream;

                if (PKGStream is null)
                {
                    if (SourcePackage.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (SourcePackage.IsFilePath())
                        {
                            InputType = Source.File | Source.JSON;
                            PKGStream = SplitHelper.OpenLocalJSON(SourcePackage);
                        }
                        else
                        {
                            PKGStream = SplitHelper.OpenRemoteJSON(SourcePackage);
                            InputType = Source.URL | Source.JSON;
                        }
                    }
                    else if (SourcePackage.IsFilePath())
                    {
                        PKGStream = File.Open(SourcePackage, FileMode.Open, FileAccess.Read, FileShare.Read);
                        InputType = Source.File;
                    }
                    else
                    {
                        InputType = Source.URL;

                        var FHStream = await LoadUrl(SourcePackage);

                        if (FHStream == null)
                            throw new AbortException("Failed to Load the URL");

                        LimitedFHost = FHStream.SingleConnection;
                        PKGStream = FHStream;

                    }
                }

                if (LimitedFHost)
                {
                    if (PKGStream is FileHostStream)
                        ((FileHostStream) PKGStream).KeepAlive = true;
                    
                    var DownTask = Downloader.CreateTask(SourcePackage, PKGStream);

                    while (DownTask.SafeLength == 0 && DownTask.Running)
                        await Task.Delay(100);

                    InputType |= Source.DiskCache;
                    PKGStream = new VirtualStream(DownTask.OpenRead(), 0, DownTask.SafeLength) {ForceAmount = true};
                }

                if (LimitedFHost && !BadHostAlert)
                {
                    await MessageBox.ShowAsync("This Filehosting is limited, Even though it is compatible with DirectPackageInstaller it is not recommended for use, prefer services like alldebrid to download from this server, otherwise you may have connection and/or speed problems.\nDon't expect to compressed files works as expected as well, the DirectPackageInstaller will need download the entire file before can do anything", "Bad File Hosting Service", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    BadHostAlert = true;
                }

                byte[] Magic = new byte[4];
                await PKGStream.ReadAsync(Magic, 0, Magic.Length);
                PKGStream.Position = 0;

                if (LimitedFHost && Common.DetectCompressionFormat(Magic) != CompressionFormat.None)
                    await MessageBox.ShowAsync("You're trying open a compressed file from a limited file hosting,\nMaybe the compressed file must be fully downloaded to open it.", "Bad File Hosting Service", MessageBoxButtons.OK, MessageBoxIcon.Stop);

                ArchiveDataInfo? DataInfo = null;
                switch (Common.DetectCompressionFormat(Magic))
                {
                    case CompressionFormat.RAR:
                        InputType |= Source.RAR;
                        await SetStatus(LimitedFHost ? "Downloading... (It may take a while)" : "Decompressing...");
                        DataInfo = await Decompressor.UnrarPKG(PKGStream, SourcePackage, async (s) => await SetStatus(s),ForcedSource);
                        break;
                    case CompressionFormat.SevenZip:
                        InputType |= Source.SevenZip;
                        await SetStatus(LimitedFHost ? "Downloading... (It may take a while)" : "Decompressing...");
                        DataInfo = await Decompressor.Un7zPKG(PKGStream, SourcePackage,async (s) => await SetStatus(s), ForcedSource);
                        break;
                }

                if (DataInfo != null)
                {
                    if (App.IsAndroid && Model.SegmentedMode)
                    {
                        await MessageBox.ShowAsync("Download of compressed files in android isn't stable, Segmented Download Will be disabled", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Model.SegmentedMode = false;
                    }
                    PKGStream = DataInfo?.Buffer ?? throw new Exception();
                    Installer.EntryFileName = Path.GetFileName(DataInfo?.Filename);
                    CurrentDecompressor = DataInfo?.Archive;
                    CurrentDecompressorVolumes = DataInfo?.Volumes;
                    ListEntries(Installer.CurrentFileList = DataInfo?.PKGList ?? throw new AbortException("Failed to list compressed files"));
                }

                await SetStatus("Reading PKG...");

                var Info = Installer.CurrentPKG = PKGStream.GetPKGInfo() ?? throw new AbortException("Failed to read the PKG information");

                await SetStatus(Info.Description);

                Model.PKGParams.Clear();
                Model.PKGParams = Info.Params;

                IconBox.Source = Info.Icon;

                if (!App.IsSingleView)
                {
                    Parent.BringIntoView();
                    Parent.Focus();
                    Parent.Activate();
                }

                btnLoad.Content = "Install";
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.Combine(App.WorkingDirectory, "DPI-ERROR.log"), ex.ToString());
                
                if (ex is not AbortException)
                    await MessageBox.ShowAsync(ex.ToString(), "DirectPackageInstaller - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else if (App.Config.ShowError)
                    await MessageBox.ShowAsync(ex.Message, "DirectPackageInstaller - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                IconBox.Source = null;
                Model.PKGParams = new List<PkgParamInfo>();

                InputType = Source.NONE;
                btnLoad.Content = "Load";

                PackagesMenu.IsVisible = false;
#if DEBUG
                throw;
#endif

                await SetStatus("Failed to Open the PKG");
            }
            finally
            {
                tbURL.IsEnabled = true;
                btnLoad.IsEnabled = true;
            }

            PKGStream?.Close();
        }

        private async Task<FileHostStream?> LoadUrl(string SourcePackage)
        {
            await SetStatus("Analyzing Urls...");

            var UrlInfo = await URLAnalyzer.Analyze(SourcePackage, false);

            while (!UrlInfo.Ready && !UrlInfo.Failed)
            {
                await SetStatus($"Analyzing Urls... {UrlInfo.Progress}");
                await Task.Delay(100);
            }

            if (UrlInfo.Failed)
            {
                URLAnalyzer.URLInfos.Remove(UrlInfo.MainURL);
                throw new AbortException("Failed to load URL info");
            }

            UrlInfo.ApplyFormatFilter();

            FileHostStream FHStream;

            if (UrlInfo.Urls.Any(x => x.SingleConnection))
            {
                FHStream = UrlInfo.Urls.First().Stream();
            }
            else
            {

                var ValidExt = UrlInfo.Urls.Select(x => x.Filename?.ToLowerInvariant()).Where(FName =>
                   string.IsNullOrWhiteSpace(FName)
                || FName.EndsWith(".rar")
                || FName.EndsWith(".7z") 
                || FName.EndsWith(".pkg")).Any();

                if (!ValidExt)
                {
                    if (UrlInfo.Urls.Length == 1)
                    {
                        await MessageBox.ShowAsync($"The input file has a unsupported format,\nSupported Files: .rar .7z .pkg", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return null;
                    }

                    var Rar = UrlInfo.Urls.SortRarFiles().ToArray();
                    var SevenZip = UrlInfo.Urls.Sort7zFiles().ToArray();

                    if (Rar.Length > 0)
                    {
                        SourcePackage = Rar.First().URL;
                        UrlInfo.Urls = Rar;
                    }

                    if (SevenZip.Length > 0)
                    {
                        SourcePackage = SevenZip.First().URL;
                        UrlInfo.Urls = SevenZip;
                    }

                    if (UrlInfo.Urls.Any(x => x.Filename.EndsWith(".pkg", StringComparison.InvariantCultureIgnoreCase)))
                    {
                        SourcePackage = UrlInfo.Links.First();
                    }

                    URLAnalyzer.URLInfos[SourcePackage] = UrlInfo;
                }
                else
                {
                    SourcePackage = UrlInfo.Links.First();
                }

                FHStream = new FileHostStream(SourcePackage);
            }
            
            return FHStream;
        }

        private async void ModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Model == null)
                return;

            //Notificações da biblioteca e do resumo do pacote não são configurações
            if (e.PropertyName is { } Name && Name != "LibraryPath"
                && (Name.StartsWith("Library") || Name.StartsWith("Pkg") || Name == "HasPkg" || Name == "HasLibraryStatus" || Name == "ShowLibraryToolbar"
                    || Name.StartsWith("Transfer") || Name.StartsWith("History") || Name.StartsWith("Console") || Name == "IsScanningConsoles" || Name == "CanCancelTransfer"
                    || Name == "ShowHistory" || Name == "ShowHistoryEmpty"
                    || Name == "ShowLibrary" || Name == "ShowPackage" || Name == "HasLibraryPath"
                    || Name == "ShowLibraryEmpty" || Name == "IsScanningLibrary"))
                return;

            switch (e.PropertyName)
            {
                case "LibraryPath":
                    App.Config.LibraryPath = Model.LibraryPath;
                    App.SaveSettings();
                    _ = RefreshLibrary();
                    return;
                case "ProxyMode":
                    App.Config.ProxyDownload = Model.ProxyMode;
                    break;
                case "SegmentedMode":
                    App.Config.SegmentedDownload = Model.SegmentedMode;
                    break;
                case "ShowTransferProgress":
                    App.Config.ShowTransferProgress = Model.ShowTransferProgress;
                    break;
                case "PS4IP":
                    App.Config.PSIP = Model.PS4IP;
                    _ = RefreshConsoleStatus();
                    break;
                case "PCIP":
                    App.Config.PCIP = Model.PCIP;
                    break;
                case "UseAllDebrid":
                    App.Config.UseAllDebrid = Model.UseAllDebrid;
                    break;
                case "UseRealDebrid":
                    App.Config.UseRealDebrid = Model.UseRealDebrid;
                    break;
                case "UseDebridLink":
                    App.Config.UseDebridLink = Model.UseDebridLink;
                    break;
                case "AllDebridApiKey":
                    App.Config.AllDebridApiKey = Model.AllDebridApiKey;
                    break;
                case "RealDebridApiKey":
                    App.Config.RealDebridApiKey = Model.RealDebridApiKey;
                    break;
                case "DebridLinkApiKey":
                    App.Config.DebridLinkApiKey = Model.DebridLinkApiKey;
                    break;
                case "DHCPService":
                    App.Config.EnableDHCP = Model.DHCPService;
                    break;
                case "CurrentURL":
                    UrlChanged(Model.CurrentURL);
                    return;
            }

            if (App.IsAndroid)
                App.SaveSettings();
        }

        private void BtnSegmentedDownloadOnClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;
            
            Model.SegmentedMode = !Model.SegmentedMode;
        }

        private void BtnTransferProgressOnClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;

            Model.ShowTransferProgress = !Model.ShowTransferProgress;
        }

        private void BtnProxyDownloadOnClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;
            
            Model.ProxyMode = !Model.ProxyMode;
        }

        private void BtnAllDebirdEnabledOnClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;
            
            Model.UseAllDebrid = !Model.UseAllDebrid;
        }
        
        private void BtnRealDebirdEnabledOnClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;
            
            Model.UseRealDebrid = !Model.UseRealDebrid;
        }

        private void BtnDebirdLinkEnabledOnClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null)
                return;

            Model.UseDebridLink = !Model.UseDebridLink;
        }

        private async Task<bool> Install(string URL, bool Silent)
        {
            if (string.IsNullOrWhiteSpace(App.Config.PSIP) || string.IsNullOrWhiteSpace(App.Config.PCIP))
            {
                await MessageBox.ShowAsync(Parent, "Failed to detect your playstation IP.\nPlease, Type your PS/PC IP in the options menu", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            var Pkg = Installer.CurrentPKG;
            var PkgVersion = Pkg.Params?.FirstOrDefault(x => x.Name == "VERSION")?.Value ?? "";
            TransferMonitor.Instance.Begin(Pkg.FriendlyName ?? "", Pkg.TitleID ?? "", PkgVersion, SourceLabel(), Pkg.PackageSize);

            var OriStatus = Status.Text;
            btnLoad.Content = "Pushing...";
            PackagesMenu.IsEnabled = false;
            btnLoad.IsEnabled = false;
            tbURL.IsEnabled = false;
            try
            {
                if (!await IPHelper.IsRPIOnline(App.Config.PSIP)) {
                    if (!await IPHelper.IsGoldHENOnline(App.Config.PSIP) && !Installer.Payload.ClientRunning)
                    {
                        if (!await IPHelper.IsEtaHenOnline(App.Config.PSIP))
                        {
                            await MessageBox.ShowAsync($"RPI, GoldHEN nor etaHEN Found at {App.Config.PSIP}, Verify if is running.\nYou may need re-enable the payload server.", "DirectPackageInstaller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return false;
                        }
                    }
                }

                var Sent = await Installer.PushPackage(App.Config, InputType, PKGStream!, URL, CurrentDecompressor, CurrentDecompressorVolumes, SetStatus, () => Status.Text, Silent);

                if (Sent)
                    TransferMonitor.Instance.MarkSent();

                return Sent;
            }
            catch
            {
                return false;
            }
            finally
            {
                //Saiu sem enviar (console offline, erro, cancelado na preparação)
                if (TransferMonitor.Instance.Phase == TransferPhase.Preparing)
                    TransferMonitor.Instance.MarkFailed();

                Model?.ApplyTransfer(TransferMonitor.Instance);

                await SetStatus(OriStatus);
                PackagesMenu.IsEnabled = true;
                btnLoad.IsEnabled = true;
                tbURL.IsEnabled = true;
                btnLoad.Content = "Install";
            }
        }
        
        private async void BtnInstallAllOnClick(object? sender, RoutedEventArgs? e)
        {
            if (e != null)
            {
                App.Callback(() => BtnInstallAllOnClick(sender, null));
                return;
            }

            if (!BatchList.Any() && (Installer.CurrentFileList == null || CurrentDecompressor == null))
                return;

            bool ErrorIgnored = false;

            var Files = Installer.CurrentFileList ?? BatchList.Select(x => x.Source);

            foreach (var File in Files.ToArray())
            {
                var ContentSource = tbURL.Text;
                
                if (string.IsNullOrWhiteSpace(ContentSource))
                    ContentSource = File;
                else
                    Installer.EntryFileName = File;

                if (InputType.HasFlag(Source.SevenZip) || InputType.HasFlag(Source.RAR))
                {
                    Stream? Stream = null;
                    
                    try
                    {
                        var Entry = CurrentDecompressor!.Entries.Single(x => x.Key.EndsWith(File, StringComparison.InvariantCultureIgnoreCase));
                        Stream = Entry.OpenEntryStream();

                        if (!Stream.CanSeek)
                            Stream = new ReadSeekableStream(Stream, TempHelper.GetTempFile(null));

                        Installer.CurrentPKG = Stream.GetPKGInfo() ?? throw new Exception();
                    }
                    catch
                    {
                        continue;
                    }
                    finally
                    {
                        Stream?.Close();
                    }
                } 
                else if (InputType.HasFlag(Source.File))
                {
                    ContentSource = File;
                    
                    try
                    {
                        using FileStream Stream = new FileStream(File, FileMode.Open);
                        Installer.CurrentPKG = Stream.GetPKGInfo() ?? throw new Exception();
                    } 
                    catch 
                    { 
                        continue; 
                    }
                }

                if (!await Install(ContentSource, true) && !ErrorIgnored)
                {
                    var Reply = await MessageBox.ShowAsync("Continue trying install the others packages?","DirectPackageInstaller", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (Reply != DialogResult.Yes){
                        break;
                    }

                    ErrorIgnored = true;
                }

                if (Installer.Server != null)
                {
                    bool Decompressing() => Installer.Server?.Decompress.Tasks.Any(x => x.Value.Running) ?? false;
                    
                    while (Decompressing() || Installer.Server.Connections > 0 || PS4IdleSeconds() > 15)
                        await Task.Delay(5000);
                }
            }

            Toast.Show("Packages sent", "All packages in the list were sent to the console", ToastKind.Success);
        }

        private static int PS4IdleSeconds()
        {
            if (Installer.Server?.LastRequest == null)
                return int.MaxValue;
            return (int)(DateTime.Now - (Installer.Server!.LastRequest!.Value)).TotalSeconds;
        }

        private void ListEntries(string[]? PKGList)
        {
            if (PKGList == null || PackagesMenu.Items == null)
                return;
            
            PackagesMenu.IsVisible = PKGList.Length > 1;
            
            if (PackagesMenu.IsVisible)
            {
                List<TemplatedControl> ToRemove = new List<TemplatedControl>();
                List<TemplatedControl> Items = PackagesMenu.Items.Cast<TemplatedControl>().ToList();
                
                foreach (var Item in Items)
                {
                    if (Item is Separator)
                        break;
                    
                    ToRemove.Add(Item);
                }

                foreach (var Item in ToRemove)
                {
                    Items.Remove(Item);
                }

                foreach (var Entry in PKGList.OrderByDescending(x=>x))
                {
                    var Item = new MenuItem()
                    {
                        Header = Path.GetFileName(Entry),
                        DataContext = Entry
                    };
                    
                    Item.Click += onInstallListItemClicked;

                    Items.Insert(0, Item);
                }
                PackagesMenu.Items.Clear();
                PackagesMenu.ItemsSource = Items;
            }
        }
        
        private void onInstallListItemClicked(object? sender, RoutedEventArgs e)
        {
            if (sender == null)
                return;

            var Item = (MenuItem)sender;
            var Entry = (string?)Item.DataContext;

            var OriSource = InputType;
            InputType = Source.NONE;

            if (OriSource.HasFlag(Source.RAR) || OriSource.HasFlag(Source.SevenZip))
            {
                var FileEntry = CurrentDecompressor!.Entries.First(x => x.Key.EndsWith(Entry, StringComparison.InvariantCultureIgnoreCase));
                Installer.EntryFileName = Path.GetFileName(FileEntry.Key);
                var Stream = new ReadSeekableStream(FileEntry.OpenEntryStream());

                App.Callback(() =>
                {
                    BtnLoadOnClick(Stream, null);

                    //Restore variables that is modified by the BtnLoadOnClick event
                    InputType = OriSource;
                    Installer.EntryFileName = Path.GetFileName(FileEntry.Key);
                });
                return;
            }

            tbURL.Text = Entry;
            App.Callback(() => BtnLoadOnClick(Entry, null));
        }

        private void UrlChanged(string Url)
        {
            InputType = Source.NONE;

            btnLoad.Content = (string.IsNullOrWhiteSpace(Url) && !File.Exists(Url)) ? "Open" : "Load";
            
            Installer.CurrentFileList = null;
            LastForcedSource = null;

            PackagesMenu.IsVisible = BatchList.Any();

            if (Url.Contains("\n"))
            {
                var Links = Url.Replace("\r\n", "\n").Split('\n')
                    .Select(x => x.Trim())
                    .Where(x => x.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
                    .ToArray();
                
                if (!Links.Any())
                {
                    App.Callback(() => Model!.CurrentURL = "");
                    return;
                }

                App.Callback(() =>
                {
                    Model!.CurrentURL = Links.First();
                    _ = App.RunInNewThread(() => _ = URLAnalyzer.Analyze(Links, false));
                });
                return;
            }

            if (Url.StartsWith("http", StringComparison.InvariantCultureIgnoreCase) && !Url.IsValidURL()) {
                int PathOrQueryPos = Url.IndexOfAny(new char[] { '/', '?' }, Url.IndexOf("://", StringComparison.Ordinal) + 3);
                
                if (PathOrQueryPos < 0)
                    return;
                
                var Host = Url.Substring(0, PathOrQueryPos);

                var PathAndQuery = Url.Substring(PathOrQueryPos);


                var PathOnly = Uri.UnescapeDataString(PathAndQuery.Split('?').First());
                var QueryOnly = PathAndQuery.Contains('?') ? PathAndQuery.Substring(PathAndQuery.IndexOf('?')) : "";

                PathOnly = Uri.EscapeDataString(PathOnly);

                Dictionary<string, string> PathReplaces = new Dictionary<string, string>()
                { 
                    { "%2f", "/" }, { "%2F", "/" }, { "[", "%5b" }, { "]", "%5d" }, { "%2b", "+" }, { "%2B", "+" },
                    { "%28", "(" }, { "%29", ")" } 
                };

                Dictionary<string, string> QueryReplaces = new Dictionary<string, string>()
                {
                    { "%2f", "/" }, { "%2F", "/" }, { "[", "%5b" }, { "]", "%5d" }
                };

                foreach (var Pair in PathReplaces)
                    PathOnly = PathOnly.Replace(Pair.Key, Pair.Value);

                foreach (var Pair in QueryReplaces)
                    QueryOnly = QueryOnly.Replace(Pair.Key, Pair.Value);

                var NewUrl = $"{Host}{PathOnly}{QueryOnly}";
                if (NewUrl.IsValidURL())
                    Model!.CurrentURL = NewUrl;
            }

            PKGStream?.Close();
            PKGStream?.Dispose();
        }


        public async Task SetStatus(string Status) {
            
            if (!Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(async () => await SetStatus(Status));
                return;
            }

            try
            {
                this.Status.Text = Status;
                await App.DoEvents();
            }
            catch
            {
                App.Callback(() => this.Status.Text = Status);
            }
        }

        private void ServerOnTransferProgressChanged(TransferProgressInfo Info) => TransferMonitor.Instance.OnProgress(Info);

        // ---- Envio: cancelar e histórico ----

        /// <summary>Interrompe o envio atual neste PC (preparação e entrega ao console).</summary>
        public void CancelTransfer()
        {
            Installer.CancelRequested = true;
            Installer.Server?.CancelTransfers();
            TransferMonitor.Instance.MarkCancelled();
            Model?.ApplyTransfer(TransferMonitor.Instance);
        }

        private void CancelTransferClick(object? sender, RoutedEventArgs e) => CancelTransfer();

        private void ToastCloseClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ToastItem Item)
                Toast.Dismiss(Item);
        }

        private void OnTransferSessionEnded(TransferSession Session)
        {
            var Entry = new HistoryEntry
            {
                Date = Session.Date,
                Title = Session.Title,
                TitleId = Session.TitleId,
                Version = Session.Version,
                Source = Session.Source,
                Size = Session.TotalBytes,
                Result = Session.Result switch
                {
                    TransferPhase.Finished => "COMPLETED",
                    TransferPhase.Direct => "SENT",
                    TransferPhase.Cancelled => "CANCELLED",
                    _ => "FAILED"
                }
            };

            if (Session.Result == TransferPhase.Finished)
                Toast.Show("Download finished", $"{Session.Title} — the console is installing it", ToastKind.Success, 8);
            else if (Session.Result == TransferPhase.Cancelled)
                Toast.Show("Transfer cancelled", Session.Title);

            Dispatcher.UIThread.Post(() =>
            {
                if (Model == null)
                    return;

                Model.AddHistory(Entry);
                HistoryStore.Save(Model.History);
            });
        }

        private void TabHistoryClick(object? sender, RoutedEventArgs e)
        {
            if (Model != null)
                Model.ShowHistory = true;
        }

        private async void ClearHistoryClick(object? sender, RoutedEventArgs e)
        {
            if (Model == null || Model.History.Count == 0)
                return;

            if (await MessageBox.ShowAsync("Clear the whole send history?", "DirectPackageInstaller", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Model.ClearHistory();
            HistoryStore.Save(Model.History);
        }

        private string SourceLabel()
        {
            if (InputType.HasFlag(Source.File))
                return "FILE";

            if (InputType.HasFlag(Source.RAR) || InputType.HasFlag(Source.SevenZip))
                return "ARCHIVE";

            if (InputType.HasFlag(Source.JSON))
                return "MANIFEST";

            return "URL";
        }

        private async void RestartServer_OnClick(object? sender, RoutedEventArgs? e)
        {
            if (Model == null)
                return;
            
            try
            {
                Installer.Server?.Stop();

                PS4Server pS4Server = new PS4Server(Model.PCIP);
                Installer.Server = pS4Server;
                Installer.Server.Start();
            }
            catch (Exception ex){
                await MessageBox.ShowAsync($"Failed to restart the server, Report a Bug:\n{ex.ToString()}", "Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnExitOnClick(object? sender, RoutedEventArgs? e)
        {
            App.SaveSettings();
            TempHelper.Clear();
            Environment.Exit(0);
        }
        
        private void tbURLOnGotFocus(object? sender, GotFocusEventArgs e)
        {
            if (!App.IsAndroid)
                return;
            
            App.Callback(() =>
            {
                tbURL.Text = App.GetClipboardText();
            });
        }

        private void PreviewGridOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name != "Bounds")
                return;
            
            InLandscape = null;
            InvalidateMeasure();
        }

        bool? InLandscape = null;
        protected override Size MeasureCore(Size availableSize)
        {
            bool ChangeToLandscape = availableSize.Width > availableSize.Height; 
            if (ChangeToLandscape != InLandscape)
            {
                if (ChangeToLandscape)
                {
                    InLandscape = true;
                    PreviewGrid.RowDefinitions = new RowDefinitions()
                    {
                        new RowDefinition()
                    };
                    
                    var IconSize = PreviewGrid.Bounds.Height;
                    if (IconSize == 0)
                        IconSize = Double.PositiveInfinity;

                    var MaxWidth = availableSize.Width / 2;
                    
                    PreviewGrid.ColumnDefinitions = new ColumnDefinitions()
                    {
                        MaxWidth > IconSize ? new ColumnDefinition(IconSize, GridUnitType.Pixel) : new ColumnDefinition(),
                        new ColumnDefinition(App.IsAndroid ? 10f : 5f, GridUnitType.Pixel),
                        new ColumnDefinition()
                    };

                    IconBox[Grid.RowProperty] = 0;
                    IconBox[Grid.ColumnProperty] = 0;
                    
                    MainGridSplitter[Grid.RowProperty] = 0;
                    MainGridSplitter[Grid.ColumnProperty] = 1;
                    
                    PkgInfoPanel[Grid.ColumnProperty] = 2;
                    PkgInfoPanel[Grid.RowProperty] = 0;
                }
                else
                {
                    InLandscape = false;

                    var IconSize = PreviewGrid.Bounds.Width;
                    if (IconSize == 0)
                        IconSize = Double.PositiveInfinity;

                    var MaxHeight = availableSize.Height / 2;
                    
                    PreviewGrid.RowDefinitions = new RowDefinitions()
                    {
                        MaxHeight > IconSize ? new RowDefinition(IconSize, GridUnitType.Pixel) : new RowDefinition(),
                        new RowDefinition(App.IsAndroid ? 10f : 5f, GridUnitType.Pixel),
                        new RowDefinition()
                    };
                    PreviewGrid.ColumnDefinitions = new ColumnDefinitions()
                    {
                        new ColumnDefinition()
                    };

                    IconBox[Grid.RowProperty] = 0;
                    IconBox[Grid.ColumnProperty] = 0;
                    
                    MainGridSplitter[Grid.RowProperty] = 1;
                    MainGridSplitter[Grid.ColumnProperty] = 0;
                    
                    PkgInfoPanel[Grid.ColumnProperty] = 0;
                    PkgInfoPanel[Grid.RowProperty] = 2;
                }
            }
            else if (InLandscape != null)
            {
                var CurrentSize = InLandscape == true ? PreviewGrid.Bounds.Height : PreviewGrid.Bounds.Width;
                var MaxSize = InLandscape == true ? availableSize.Width / 2 : availableSize.Height / 2;
                if (CurrentSize > MaxSize)
                {
                    InLandscape = null;
                    return MeasureCore(availableSize);
                }
            }

            return base.MeasureCore(availableSize);
        }
    }
}
