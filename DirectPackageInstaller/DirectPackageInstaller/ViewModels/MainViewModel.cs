using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using DirectPackageInstaller.Host;
using ReactiveUI;

namespace DirectPackageInstaller.ViewModels
{
    public class MainViewModel : ReactiveObject
    {

        private string _PS4IP = null;
        public string PS4IP
        {
            get => _PS4IP;
            set
            {
                _PS4IP = value;
                
                if (IPAddress.TryParse(value, out _))
                    PCIP = IPHelper.FindLocalIP(value) ?? PCIP;
                
                this.RaisePropertyChanged();
            }
        }
        
        
        private string _PCIP = null;
        public string PCIP
        {
            get => _PCIP;
            set => this.RaiseAndSetIfChanged(ref _PCIP, value);
        }

        private string _CurrentURL = null;
        public string CurrentURL
        {
            get => _CurrentURL;
            set
            {
                _CurrentURL = value;
                this.RaisePropertyChanged();
            }
        }

        private string _allDebridApiKey = null;
        public string AllDebridApiKey
        {
            get => _allDebridApiKey;
            set => this.RaiseAndSetIfChanged(ref _allDebridApiKey, value);
        }

        private string _realDebridApiKey = null;
        public string RealDebridApiKey
        {
            get => _realDebridApiKey;
            set => this.RaiseAndSetIfChanged(ref _realDebridApiKey, value);
        }

        private string _debridLinkApiKey = null;
        public string DebridLinkApiKey
        {
            get => _debridLinkApiKey;
            set => this.RaiseAndSetIfChanged(ref _debridLinkApiKey, value);
        }

        private bool _ProxyMode = false;
        public bool ProxyMode 
        {
            get => _ProxyMode;
            set => this.RaiseAndSetIfChanged(ref _ProxyMode, value);
        }

        private bool _SegmentedMode = false;
        public bool SegmentedMode 
        {
            get => _SegmentedMode;
            set => this.RaiseAndSetIfChanged(ref _SegmentedMode, value);
        }

        private bool _ShowTransferProgress = true;
        public bool ShowTransferProgress
        {
            get => _ShowTransferProgress;
            set => this.RaiseAndSetIfChanged(ref _ShowTransferProgress, value);
        }
        
        private bool _useAllDebrid = false;
        public bool UseAllDebrid 
        {
            get => _useAllDebrid;
            set => this.RaiseAndSetIfChanged(ref _useAllDebrid, value);
        }
        
        private bool _useRealDebrid = false;
        public bool UseRealDebrid 
        {
            get => _useRealDebrid;
            set => this.RaiseAndSetIfChanged(ref _useRealDebrid, value);
        }

        private bool _useDebridLink = false;
        public bool UseDebridLink
        {
            get => _useDebridLink;
            set => this.RaiseAndSetIfChanged(ref _useDebridLink, value);
        }

        private bool _DHCPService = false;
        public bool DHCPService
        {
            get => _DHCPService;
            set => this.RaiseAndSetIfChanged(ref _DHCPService, value);
        }

        /// <summary>Notificações visíveis (ver Others/Toast.cs).</summary>
        public ObservableCollection<ToastItem> Toasts => Toast.Items;

        // ---- Histórico de envios ----

        public ObservableCollection<HistoryEntry> History { get; } = new ObservableCollection<HistoryEntry>();

        public string HistoryTabText => History.Count > 0 ? $"HISTORY  {History.Count}" : "HISTORY";

        public bool ShowHistoryEmpty => History.Count == 0;

        public void SetHistory(IEnumerable<HistoryEntry> Entries)
        {
            History.Clear();

            foreach (var Entry in Entries)
                History.Add(Entry);

            HistoryChanged();
        }

        public void AddHistory(HistoryEntry Entry)
        {
            History.Insert(0, Entry);
            HistoryChanged();
        }

        public void ClearHistory()
        {
            History.Clear();
            HistoryChanged();
        }

        private void HistoryChanged()
        {
            this.RaisePropertyChanged(nameof(HistoryTabText));
            this.RaisePropertyChanged(nameof(ShowHistoryEmpty));
            UpdateSentFlags();
        }

        /// <summary>Marca na biblioteca o que já foi enviado ao console.</summary>
        private void UpdateSentFlags()
        {
            foreach (var Item in _libraryAll)
            {
                Item.IsSent = Item.TitleId.Length > 0 && History.Any(x => x.Delivered
                    && x.TitleId == Item.TitleId && x.Version == Item.Version);
            }
        }

        // ---- Estado do envio atual (barra de estado) ----

        private bool _transferVisible;
        public bool TransferVisible
        {
            get => _transferVisible;
            private set => this.RaiseAndSetIfChanged(ref _transferVisible, value);
        }

        private bool _canCancelTransfer;
        public bool CanCancelTransfer
        {
            get => _canCancelTransfer;
            private set => this.RaiseAndSetIfChanged(ref _canCancelTransfer, value);
        }

        private bool _transferShowBar;
        public bool TransferShowBar
        {
            get => _transferShowBar;
            private set => this.RaiseAndSetIfChanged(ref _transferShowBar, value);
        }

        private bool _transferIndeterminate;
        public bool TransferIndeterminate
        {
            get => _transferIndeterminate;
            private set => this.RaiseAndSetIfChanged(ref _transferIndeterminate, value);
        }

        private double _transferProgress;
        public double TransferProgress
        {
            get => _transferProgress;
            private set => this.RaiseAndSetIfChanged(ref _transferProgress, value);
        }

        private string _transferText = "";
        public string TransferText
        {
            get => _transferText;
            private set => this.RaiseAndSetIfChanged(ref _transferText, value);
        }

        public void ApplyTransfer(TransferMonitor Monitor)
        {
            var Phase = Monitor.Phase;

            TransferVisible = Phase != TransferPhase.Idle;
            CanCancelTransfer = Monitor.IsActive;
            TransferIndeterminate = Phase is TransferPhase.Preparing or TransferPhase.Waiting;
            TransferShowBar = ShowTransferProgress && Monitor.IsActive;
            TransferProgress = Monitor.Progress;
            TransferText = Phase switch
            {
                TransferPhase.Preparing => "Preparing...",
                TransferPhase.Waiting => "Sent. Waiting for the console to start the download...",
                TransferPhase.Transferring => DescribeTransfer(Monitor),
                TransferPhase.Finished => "Download finished. The console is installing it; check its notifications.",
                TransferPhase.Direct => "Sent. The console downloads straight from the source; progress isn't visible here.",
                TransferPhase.Failed => "Failed to send the package.",
                TransferPhase.Cancelled => "Cancelled on this PC. If the console already queued it, remove it in the console's notifications.",
                _ => ""
            };
        }

        private static string DescribeTransfer(TransferMonitor Monitor)
        {
            var Text = $"{Monitor.Progress:P0}  ·  {TransferProgressInfo.FormatBytes(Monitor.ServedBytes)} / {TransferProgressInfo.FormatBytes(Monitor.TotalBytes)}";

            if (Monitor.BytesPerSecond >= 1)
                Text += $"  ·  {TransferProgressInfo.FormatBytes(Monitor.BytesPerSecond)}/s";

            if (Monitor.Eta is { } Eta)
            {
                Text += Eta.TotalHours >= 1
                    ? $"  ·  {(int)Eta.TotalHours}h {Eta.Minutes:00}m left"
                    : $"  ·  {Eta.Minutes}m {Eta.Seconds:00}s left";
            }

            return Text;
        }

        // ---- Biblioteca: pasta fixa com os PKGs organizados em grade ----

        private readonly List<LibraryItem> _libraryAll = new List<LibraryItem>();

        /// <summary>Itens exibidos na grade (já filtrados pela busca e ordenados por título).</summary>
        public ObservableCollection<LibraryItem> Library { get; } = new ObservableCollection<LibraryItem>();

        private string? _libraryPath = null;
        public string? LibraryPath
        {
            get => _libraryPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _libraryPath, value);
                RaiseLibraryTexts();
            }
        }

        private string _librarySearch = "";
        public string LibrarySearch
        {
            get => _librarySearch;
            set
            {
                this.RaiseAndSetIfChanged(ref _librarySearch, value ?? "");
                ApplyLibraryFilter();
            }
        }

        private bool _showLibrary = false;
        public bool ShowLibrary
        {
            get => _showLibrary;
            set
            {
                if (value && _showHistory)
                {
                    _showHistory = false;
                    this.RaisePropertyChanged(nameof(ShowHistory));
                }

                this.RaiseAndSetIfChanged(ref _showLibrary, value);
                this.RaisePropertyChanged(nameof(ShowPackage));
                this.RaisePropertyChanged(nameof(ShowLibraryToolbar));
            }
        }

        private bool _showHistory = false;
        public bool ShowHistory
        {
            get => _showHistory;
            set
            {
                if (value && _showLibrary)
                {
                    _showLibrary = false;
                    this.RaisePropertyChanged(nameof(ShowLibrary));
                    this.RaisePropertyChanged(nameof(ShowLibraryToolbar));
                }

                this.RaiseAndSetIfChanged(ref _showHistory, value);
                this.RaisePropertyChanged(nameof(ShowPackage));
            }
        }

        public bool ShowPackage => !_showLibrary && !_showHistory;

        private bool _isScanningLibrary = false;
        public bool IsScanningLibrary
        {
            get => _isScanningLibrary;
            set
            {
                this.RaiseAndSetIfChanged(ref _isScanningLibrary, value);
                RaiseLibraryTexts();
            }
        }

        public bool HasLibraryPath => !string.IsNullOrWhiteSpace(LibraryPath);

        /// <summary>A barra de busca/pasta só aparece na biblioteca e com uma pasta definida.</summary>
        public bool ShowLibraryToolbar => _showLibrary && HasLibraryPath;

        public bool ShowLibraryEmpty => Library.Count == 0;

        public string LibraryTabText => _libraryAll.Count > 0 ? $"LIBRARY  {_libraryAll.Count}" : "LIBRARY";

        public string LibraryPathText => HasLibraryPath ? LibraryPath! : "No folder selected";

        public string LibraryEmptyText
        {
            get
            {
                if (!HasLibraryPath)
                    return "Choose the folder where your .pkg files are";

                if (IsScanningLibrary)
                    return "Scanning...";

                return _libraryAll.Count > 0 ? "No package matches your search" : "No .pkg files found in this folder";
            }
        }

        public bool HasLibraryStatus => LibraryStatus.Length > 0;

        /// <summary>Só mostra algo quando há informação nova: varredura em andamento ou filtro ativo.</summary>
        public string LibraryStatus
        {
            get
            {
                if (IsScanningLibrary)
                    return $"Scanning...  {_libraryAll.Count} found";

                return _libraryAll.Count > 0 && Library.Count != _libraryAll.Count
                    ? $"{Library.Count} of {_libraryAll.Count} shown"
                    : "";
            }
        }

        public void ClearLibrary()
        {
            _libraryAll.Clear();
            Library.Clear();
            RaiseLibraryTexts();
        }

        public void AddLibraryItem(LibraryItem Item)
        {
            Item.IsSent = Item.TitleId.Length > 0 && History.Any(x => x.Delivered
                && x.TitleId == Item.TitleId && x.Version == Item.Version);

            _libraryAll.Add(Item);

            if (MatchesSearch(Item))
                InsertSorted(Item);

            RaiseLibraryTexts();
        }

        public void FinishLibraryScan() => IsScanningLibrary = false;

        private bool MatchesSearch(LibraryItem Item)
        {
            var Query = (LibrarySearch ?? "").Trim();

            if (Query.Length == 0)
                return true;

            return Item.Title.Contains(Query, StringComparison.OrdinalIgnoreCase)
                || Item.TitleId.Contains(Query, StringComparison.OrdinalIgnoreCase)
                || Item.FileName.Contains(Query, StringComparison.OrdinalIgnoreCase);
        }

        private void InsertSorted(LibraryItem Item)
        {
            int Index = 0;

            while (Index < Library.Count && string.Compare(Library[Index].Title, Item.Title, StringComparison.OrdinalIgnoreCase) <= 0)
                Index++;

            Library.Insert(Index, Item);
        }

        private void ApplyLibraryFilter()
        {
            Library.Clear();

            foreach (var Item in _libraryAll.Where(MatchesSearch).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
                Library.Add(Item);

            RaiseLibraryTexts();
        }

        private void RaiseLibraryTexts()
        {
            this.RaisePropertyChanged(nameof(HasLibraryPath));
            this.RaisePropertyChanged(nameof(ShowLibraryToolbar));
            this.RaisePropertyChanged(nameof(ShowLibraryEmpty));
            this.RaisePropertyChanged(nameof(LibraryTabText));
            this.RaisePropertyChanged(nameof(LibraryPathText));
            this.RaisePropertyChanged(nameof(LibraryEmptyText));
            this.RaisePropertyChanged(nameof(LibraryStatus));
            this.RaisePropertyChanged(nameof(HasLibraryStatus));
        }

        private List<PkgParamInfo> _PKGParams = new List<PkgParamInfo>();
        public List<PkgParamInfo> PKGParams
        {
            get => _PKGParams;
            set
            {
                this.RaiseAndSetIfChanged(ref _PKGParams, value);

                this.RaisePropertyChanged(nameof(HasPkg));
                this.RaisePropertyChanged(nameof(PkgTitle));
                this.RaisePropertyChanged(nameof(PkgSubtitle));
                this.RaisePropertyChanged(nameof(PkgCategory));
                this.RaisePropertyChanged(nameof(HasPkgCategory));
                this.RaisePropertyChanged(nameof(PkgDetails));
                this.RaisePropertyChanged(nameof(PkgParamCount));
            }
        }

        // ---- Resumo do pacote carregado, derivado de PKGParams ----

        private string? Param(string Name) =>
            PKGParams?.FirstOrDefault(x => x.Name == Name)?.Value is { Length: > 0 } Value ? Value : null;

        public bool HasPkg => PKGParams is { Count: > 0 };

        public int PkgParamCount => PKGParams?.Count ?? 0;

        public string PkgTitle => Param("TITLE") ?? "Unknown package";

        public string PkgCategory => Param("CATEGORY") ?? "";

        public bool HasPkgCategory => PkgCategory.Length > 0;

        public string PkgSubtitle
        {
            get
            {
                var Parts = new List<string>();

                if (Param("TITLE_ID") is { } TitleId)
                    Parts.Add(TitleId);

                if (Param("VERSION") is { } Version)
                    Parts.Add("v" + Version);

                return string.Join("  ·  ", Parts);
            }
        }

        /// <summary>Campos principais, com rótulos legíveis. A tabela completa fica recolhida.</summary>
        public List<PkgParamInfo> PkgDetails
        {
            get
            {
                var Result = new List<PkgParamInfo>();

                void Add(string Label, string? Value)
                {
                    if (!string.IsNullOrWhiteSpace(Value))
                        Result.Add(new PkgParamInfo { Name = Label, Value = Value });
                }

                Add("Title ID", Param("TITLE_ID"));
                Add("Content ID", Param("CONTENT_ID"));
                Add("Version", Param("VERSION"));
                Add("App version", Param("APP_VER"));
                Add("Category", Param("CATEGORY"));
                Add("Minimum firmware", FormatSystemVersion(Param("SYSTEM_VER")));

                return Result;
            }
        }

        /// <summary>SYSTEM_VER vem em BCD (05050000 = 5.05). Zero significa sem requisito.</summary>
        private static string? FormatSystemVersion(string? Raw)
        {
            if (Raw is not { Length: 8 } || Raw.All(x => x == '0'))
                return null;

            return $"{Raw.Substring(0, 2).TrimStart('0').PadLeft(1, '0')}.{Raw.Substring(2, 2)}";
        }
    }

    public class PkgParamInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
