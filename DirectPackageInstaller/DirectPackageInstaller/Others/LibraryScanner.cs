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

    /// <summary>Já enviado ao console (consta no histórico).</summary>
    public bool IsSent
    {
        get => _isSent;
        set => this.RaiseAndSetIfChanged(ref _isSent, value);
    }

    public string Path { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Title { get; init; } = "";
    public string TitleId { get; init; } = "";
    public string Version { get; init; } = "";
    public string Kind { get; init; } = "";
    public string SizeText { get; init; } = "";
    public Bitmap? Icon { get; init; }

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
