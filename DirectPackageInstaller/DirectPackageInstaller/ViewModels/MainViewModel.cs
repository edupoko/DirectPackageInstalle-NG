using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        private bool _CNLService = false;
        public bool CNLService 
        {
            get => _CNLService;
            set => this.RaiseAndSetIfChanged(ref _CNLService, value);
        }

        private bool _DHCPService = false;
        public bool DHCPService
        {
            get => _DHCPService;
            set => this.RaiseAndSetIfChanged(ref _DHCPService, value);
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
