using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DirectPackageInstaller
{
    public struct Settings
    {
        public string EthernetAdapter;

        public string AllDebridApiKey;
        public string RealDebridApiKey;
        public string DebridLinkApiKey;
        public string PSIP;
        public string PCIP;

        public string LibraryPath;

        /// <summary>Modelo escolhido manualmente: "PS4", "PS5" ou vazio (detectar automaticamente).</summary>
        public string ConsoleModel;

        /// <summary>Último modelo detectado e o IP a que ele se refere.</summary>
        public string ConsoleType;
        public string ConsoleTypeIp;

        public int? PayloadPort;

        public bool UseAllDebrid;
        public bool UseRealDebrid;
        public bool UseDebridLink;
        public bool SearchPS4;
        public bool ProxyDownload;
        public bool SegmentedDownload;
        public bool SkipUpdateCheck;

        public bool EnableDHCP;

        public bool ShowError;
        public bool ShowTransferProgress;

        public bool AutoSplitPKG;
    }
}
