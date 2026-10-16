using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DirectPackageInstaller;

/// <summary>Um console encontrado na rede local.</summary>
public sealed class DiscoveredConsole
{
    public string Ip { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>"PS4" ou "PS5".</summary>
    public string Type { get; init; } = "PS4";

    /// <summary>IP deste PC na interface em que o console respondeu.</summary>
    public string LocalIp { get; init; } = "";

    /// <summary>Versão do sistema, ex.: 9.05 (vazio se não informada).</summary>
    public string SystemVersion { get; init; } = "";

    public string Title => string.IsNullOrWhiteSpace(Name) ? Type : Name;

    public string Details => string.Join("  ·  ", new[]
    {
        Type,
        Ip,
        SystemVersion.Length > 0 ? "firmware " + SystemVersion : ""
    }.Where(x => x.Length > 0));
}

/// <summary>
/// Varredura ativa por consoles PS4/PS5 usando o protocolo de descoberta da Sony:
/// um broadcast UDP "SRCH" nas portas 987 (PS4) e 9302 (PS5) em cada interface local.
/// Funciona com o console ligado ou em repouso com a rede ativa, na mesma rede local.
/// </summary>
public static class ConsoleScanner
{
    private const string SearchMessage = "SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00030010\n";

    public static async Task<List<DiscoveredConsole>> ScanAsync(TimeSpan Duration, CancellationToken Token = default)
    {
        var Found = new Dictionary<string, DiscoveredConsole>();

        var Locals = IPHelper.EnumLocalIPs();
        var Targets = Locals.Length == 0 ? new string?[] { null } : Locals.Cast<string?>().ToArray();

        using var Limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Limit.CancelAfter(Duration);

        await Task.WhenAll(Targets.Select(Local => ScanInterface(Local, Found, Limit.Token)));

        lock (Found)
            return Found.Values.OrderBy(x => x.Ip, StringComparer.Ordinal).ToList();
    }

    private static async Task ScanInterface(string? Local, Dictionary<string, DiscoveredConsole> Found, CancellationToken Token)
    {
        try
        {
            using var Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Socket.EnableBroadcast = true;
            Socket.Bind(new IPEndPoint(Local != null ? IPAddress.Parse(Local) : IPAddress.Any, 0));

            try
            {
                Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, 1);
            }
            catch
            {
            }

            var Broadcast = IPAddress.Broadcast;

            if (App.IsAndroid && Local != null)
                Broadcast = IPAddress.Parse(App.GetBroadcastAddress(Local));

            var Probe = System.Text.Encoding.UTF8.GetBytes(SearchMessage);

            //Repete o chamado enquanto escuta: um único pacote pode se perder
            var Sender = Task.Run(async () =>
            {
                try
                {
                    while (!Token.IsCancellationRequested)
                    {
                        await Socket.SendToAsync(Probe, SocketFlags.None, new IPEndPoint(Broadcast, 987), Token);
                        await Socket.SendToAsync(Probe, SocketFlags.None, new IPEndPoint(Broadcast, 9302), Token);
                        await Task.Delay(500, Token);
                    }
                }
                catch
                {
                }
            });

            var Buffer = new byte[2048];

            while (!Token.IsCancellationRequested)
            {
                var Received = await Socket.ReceiveFromAsync(Buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), Token);

                if (Received.RemoteEndPoint is not IPEndPoint From)
                    continue;

                var Console = Parse(System.Text.Encoding.UTF8.GetString(Buffer, 0, Received.ReceivedBytes), From.Address, Local ?? "");

                if (Console == null)
                    continue;

                lock (Found)
                    Found[Console.Ip] = Console;
            }

            await Sender;
        }
        catch
        {
            //Cancelado pelo tempo limite ou interface indisponível
        }
    }

    /// <summary>Lê a resposta de descoberta. Retorna null se não for uma resposta de console.</summary>
    public static DiscoveredConsole? Parse(string Message, IPAddress From, string LocalIp)
    {
        //O próprio chamado "SRCH" pode voltar pelo broadcast; só interessam as respostas
        if (!Message.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) || From.Equals(IPAddress.Any))
            return null;

        var Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var Line in Message.Split('\n'))
        {
            var Colon = Line.IndexOf(':');

            if (Colon <= 0)
                continue;

            Fields[Line.Substring(0, Colon).Trim()] = Line.Substring(Colon + 1).Trim();
        }

        Fields.TryGetValue("host-type", out var HostType);
        Fields.TryGetValue("host-name", out var Name);
        Fields.TryGetValue("system-version", out var Version);

        return new DiscoveredConsole
        {
            Ip = From.ToString(),
            Name = Name ?? "",
            Type = (HostType ?? "").Contains("PS5", StringComparison.OrdinalIgnoreCase) ? "PS5" : "PS4",
            LocalIp = LocalIp,
            SystemVersion = FormatVersion(Version)
        };
    }

    /// <summary>"0x09050001" vira "9.05".</summary>
    private static string FormatVersion(string? Raw)
    {
        if (Raw is not { Length: 10 } || !Raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return "";

        return int.TryParse(Raw.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var Major)
            ? $"{Major}.{Raw.Substring(4, 2)}"
            : "";
    }
}
