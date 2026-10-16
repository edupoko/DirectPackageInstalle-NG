using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DirectPackageInstaller.Host;

/// <summary>
/// Consulta ao console (pelo RPI) quais jogos da biblioteca estão instalados.
///
/// Guarda a resposta de cada Title ID por alguns minutos: jogos já consultados recebem a
/// resposta guardada na hora, e só os novos (ou vencidos) são perguntados ao console,
/// em sequência e com pausa entre as consultas.
/// </summary>
public sealed class InstalledChecker
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, (bool Exists, long Size, DateTime At)> Cache = new Dictionary<string, (bool, long, DateTime)>();
    private string CacheIp = "";
    private CancellationTokenSource? Running;

    /// <summary>Pausa entre consultas, para não sobrecarregar o console.</summary>
    public TimeSpan Spacing { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Quantas consultas foram enviadas ao console (para testes).</summary>
    public int Queries { get; private set; }

    /// <summary>Atualiza os itens. Com <paramref name="Force"/>, ignora as respostas guardadas.</summary>
    public async Task Refresh(string Ip, IEnumerable<LibraryItem> Items, bool Force = false)
    {
        Running?.Cancel();
        var Cts = Running = new CancellationTokenSource();

        if (Ip != CacheIp || Force)
        {
            Cache.Clear();
            CacheIp = Ip;
        }

        var Games = Items.Where(x => x.Kind == "GAME" && RpiClient.IsValidTitleId(x.TitleId)).Take(300).ToList();

        try
        {
            foreach (var Item in Games)
            {
                if (Cts.IsCancellationRequested)
                    return;

                if (!Cache.TryGetValue(Item.TitleId, out var Answer) || DateTime.Now - Answer.At > CacheLifetime)
                {
                    Queries++;
                    var Reply = await RpiClient.IsInstalled(Ip, Item.TitleId);

                    //O console não respondeu: para aqui; a próxima atualização tenta de novo
                    if (!Reply.Ok)
                        return;

                    Answer = Cache[Item.TitleId] = (Reply.Exists, Reply.Size, DateTime.Now);

                    if (Spacing > TimeSpan.Zero)
                        await Task.Delay(Spacing, Cts.Token);
                }

                Apply(Item, Answer.Exists, Answer.Size);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Registra uma mudança conhecida (ex.: depois de desinstalar), sem consultar o console.</summary>
    public void Remember(string TitleId, bool Exists, long Size = 0) => Cache[TitleId] = (Exists, Size, DateTime.Now);

    private static void Apply(LibraryItem Item, bool Exists, long Size)
    {
        Item.Installed = Exists;
        Item.InstalledSize = Exists && Size > 0 ? TransferProgressInfo.FormatBytes(Size) : "";
    }
}
