using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;

namespace DirectPackageInstaller;

/// <summary>Um envio registrado no histórico.</summary>
public class HistoryEntry
{
    public DateTime Date { get; init; }
    public string Title { get; init; } = "";
    public string TitleId { get; init; } = "";
    public string Version { get; init; } = "";
    public string Source { get; init; } = "";
    public string Result { get; init; } = "";
    public long Size { get; init; }

    public string DateText => Date.ToString("yyyy-MM-dd  HH:mm");

    public string SizeText => Size > 0 ? Host.TransferProgressInfo.FormatBytes(Size) : "";

    public string Subtitle => string.Join("  ·  ", new[]
    {
        TitleId,
        Version.Length > 0 ? "v" + Version : "",
        Source
    }.Where(x => x.Length > 0));

    /// <summary>Verde = concluído, azul = enviado (modo direto), vermelho = falhou, cinza = cancelado.</summary>
    public IBrush ResultBrush => new SolidColorBrush(Color.Parse(Result switch
    {
        "COMPLETED" => "#4ADE80",
        "SENT" => "#60A5FA",
        "FAILED" => "#F87171",
        "REMOVED" => "#C084FC",
        _ => "#9A9AA2"
    }));

    /// <summary>Envios que chegaram ao console (concluídos ou enviados em modo direto).</summary>
    public bool Delivered => Result is "COMPLETED" or "SENT";
}

/// <summary>
/// Histórico de envios em History.tsv (ao lado do Settings.ini).
/// Formato simples de texto, sem serialização por reflexão.
/// </summary>
public static class HistoryStore
{
    private const int MaxEntries = 500;

    private static string FilePath => Path.Combine(App.WorkingDirectory, "History.tsv");

    public static List<HistoryEntry> Load()
    {
        var Result = new List<HistoryEntry>();

        try
        {
            if (!File.Exists(FilePath))
                return Result;

            foreach (var Line in File.ReadAllLines(FilePath))
            {
                var Parts = Line.Split('\t');

                if (Parts.Length < 7 || !long.TryParse(Parts[0], out var Ticks) || !long.TryParse(Parts[6], out var Size))
                    continue;

                Result.Add(new HistoryEntry
                {
                    Date = new DateTime(Ticks),
                    Title = Parts[1],
                    TitleId = Parts[2],
                    Version = Parts[3],
                    Source = Parts[4],
                    Result = Parts[5],
                    Size = Size
                });
            }
        }
        catch
        {
        }

        return Result.OrderByDescending(x => x.Date).ToList();
    }

    public static void Save(IEnumerable<HistoryEntry> Entries)
    {
        try
        {
            var Lines = Entries
                .OrderByDescending(x => x.Date)
                .Take(MaxEntries)
                .Select(x => string.Join("\t", x.Date.Ticks, Clean(x.Title), Clean(x.TitleId), Clean(x.Version), Clean(x.Source), Clean(x.Result), x.Size));

            File.WriteAllLines(FilePath, Lines);
        }
        catch
        {
        }
    }

    private static string Clean(string Text) => (Text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
