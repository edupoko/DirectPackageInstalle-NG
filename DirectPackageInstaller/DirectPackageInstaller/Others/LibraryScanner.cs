using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Media.Imaging;
using DirectPackageInstaller.Host;
using ReactiveUI;

namespace DirectPackageInstaller;

/// <summary>Um pacote encontrado na pasta da biblioteca.</summary>
public class LibraryItem : ReactiveObject
{
    private bool _isSent;
    private bool _installed;
    private bool _rpiReady;
    private string _installedSize = "";

    /// <summary>Já enviado ao console (consta no histórico).</summary>
    public bool IsSent
    {
        get => _isSent;
        set => this.RaiseAndSetIfChanged(ref _isSent, value);
    }

    public string Path { get; init; } = "";
    public string FileName { get; init; } = "";
    public string ContentId { get; init; } = "";
    public string Title { get; init; } = "";
    public string TitleId { get; init; } = "";
    public string Version { get; init; } = "";
    public string Kind { get; init; } = "";
    public string SizeText { get; init; } = "";
    public Bitmap? Icon { get; init; }

    /// <summary>O console confirmou que o jogo está instalado (só jogos base; consulta pelo RPI).</summary>
    public bool Installed
    {
        get => _installed;
        set
        {
            this.RaiseAndSetIfChanged(ref _installed, value);
            this.RaisePropertyChanged(nameof(CanUninstall));
        }
    }

    /// <summary>Tamanho ocupado no console, ex.: "6.79 GB".</summary>
    public string InstalledSize
    {
        get => _installedSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _installedSize, value ?? "");
            this.RaisePropertyChanged(nameof(InstalledText));
        }
    }

    public string InstalledText => InstalledSize.Length > 0 ? "INSTALLED  " + InstalledSize : "INSTALLED";

    /// <summary>O console tem o Remote Package Installer ativo (necessário para desinstalar).</summary>
    public bool RpiReady
    {
        get => _rpiReady;
        set
        {
            this.RaiseAndSetIfChanged(ref _rpiReady, value);
            this.RaisePropertyChanged(nameof(CanUninstall));
        }
    }

    /// <summary>
    /// Jogos só mostram o botão quando o console confirmou que estão instalados. Atualizações e DLCs
    /// não têm essa consulta no RPI, então mostram o botão sempre que o RPI estiver ativo.
    /// </summary>
    public bool CanUninstall => RpiReady && (Kind == "GAME" ? Installed : Kind is "UPDATE" or "DLC");

    /// <summary>TITLE_ID e versão, ex.: SRCH00001  ·  v5.11</summary>
    public string Subtitle => string.Join("  ·  ", new[] { TitleId, Version.Length > 0 ? "v" + Version : "" }.Where(x => x.Length > 0));

    /// <summary>Tipo e tamanho, ex.: GAME  ·  4.2 GB</summary>
    public string Details => string.Join("  ·  ", new[] { Kind, SizeText }.Where(x => x.Length > 0));
}

/// <summary>Percorre uma pasta e lê os metadados dos arquivos .pkg encontrados.</summary>
public static class LibraryScanner
{
    private const int IconHeight = 128;

    public static void Scan(string Root, Action<LibraryItem> OnItem, CancellationToken Token)
    {
        var Options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        foreach (var File in Directory.EnumerateFiles(Root, "*.pkg", Options))
        {
            if (Token.IsCancellationRequested)
                return;

            try
            {
                OnItem(Read(File));
            }
            catch
            {
                //Arquivo ilegível ou em uso: ignora e segue para o próximo
            }
        }
    }

    private static LibraryItem Read(string File)
    {
        var FileName = System.IO.Path.GetFileName(File);

        using var Stream = new FileStream(File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var Size = Stream.Length;
        var Info = Stream.GetPKGInfo();

        if (Info == null)
        {
            return new LibraryItem
            {
                Path = File,
                FileName = FileName,
                Title = FileName,
                Kind = "UNREADABLE",
                SizeText = TransferProgressInfo.FormatBytes(Size)
            };
        }

        string? Param(string Name) => Info.Value.Params?.FirstOrDefault(x => x.Name == Name)?.Value;

        Bitmap? Icon = null;
        if (Info.Value.IconData is { Length: > 0 } Data)
        {
            try
            {
                using var Buffer = new MemoryStream(Data);
                Icon = Bitmap.DecodeToHeight(Buffer, IconHeight);
            }
            catch
            {
            }
        }

        return new LibraryItem
        {
            Path = File,
            FileName = FileName,
            Title = string.IsNullOrWhiteSpace(Info.Value.FriendlyName) ? FileName : Info.Value.FriendlyName,
            ContentId = Info.Value.ContentID ?? "",
            TitleId = Info.Value.TitleID ?? "",
            Version = Param("VERSION") ?? Param("APP_VER") ?? "",
            Kind = KindOf(Info.Value.ContentType),
            SizeText = TransferProgressInfo.FormatBytes(Size),
            Icon = Icon
        };
    }

    private static string KindOf(string? ContentType)
    {
        var Type = (ContentType ?? "").ToLowerInvariant().Trim();

        if (Type.StartsWith("gp"))
            return "UPDATE";

        if (Type == "ac")
            return "DLC";

        if (Type.StartsWith("gd"))
            return "GAME";

        return Type.Length > 0 ? Type.ToUpperInvariant() : "PKG";
    }
}
