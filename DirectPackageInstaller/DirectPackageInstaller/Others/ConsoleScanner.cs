using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DirectPackageInstaller;

/// <summary>Um console encontrado na rede local.</summary>
public sealed class DiscoveredConsole
{
    public string Ip { get; init; } = "";
    public string Name { get; set; } = "";

    /// <summary>"PS4", "PS5" ou vazio quando só os serviços foram vistos e o tipo não dá para saber.</summary>
    public string Type { get; set; } = "";

    /// <summary>IP deste PC na interface em que o console respondeu.</summary>
    public string LocalIp { get; set; } = "";

    /// <summary>Versão do sistema, ex.: 9.05 (vazio se não informada).</summary>
    public string SystemVersion { get; set; } = "";

    /// <summary>Instaladores que responderam, ex.: "GoldHEN + RPI" (vazio se só a descoberta Sony respondeu).</summary>
    public string Services { get; set; } = "";

    public string Title => !string.IsNullOrWhiteSpace(Name) ? Name : Type.Length > 0 ? Type : "Console";

    public string Details => string.Join("  ·  ", new[]
    {
        Type,
        Ip,
        SystemVersion.Length > 0 ? "firmware " + SystemVersion : "",
        Services
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

        //Descoberta Sony (Remote Play) e sondagem dos serviços, em paralelo
        var Discovery = Task.WhenAll(Targets.Select(Local => ScanInterface(Local, Found, Limit.Token)));

        await ScanServices(Found, Limit.Token);

        //A sondagem de serviços termina rápido; se já achou algo, dá só um instante
        //para a descoberta Sony complementar (nome e firmware) em vez de esperar tudo
        bool Any;
        lock (Found)
            Any = Found.Count > 0;

        if (Any)
            Limit.CancelAfter(TimeSpan.FromSeconds(1.5));

        await Discovery;

        lock (Found)
            return Found.Values.OrderBy(x => x.Ip, StringComparer.Ordinal).ToList();
    }

    private static void Add(Dictionary<string, DiscoveredConsole> Found, DiscoveredConsole Item)
    {
        lock (Found)
        {
            if (!Found.TryGetValue(Item.Ip, out var Current))
            {
                Found[Item.Ip] = Item;
                return;
            }

            //Combina as duas fontes: a descoberta Sony traz nome/tipo/firmware, os serviços trazem os instaladores
            if (Item.Name.Length > 0) Current.Name = Item.Name;
            if (Item.Type.Length > 0) Current.Type = Item.Type;
            if (Item.SystemVersion.Length > 0) Current.SystemVersion = Item.SystemVersion;
            if (Item.Services.Length > 0) Current.Services = Item.Services;
            if (Item.LocalIp.Length > 0) Current.LocalIp = Item.LocalIp;
        }
    }

    // ---- Sondagem de serviços (independe do Remote Play) ----

    private const int MaxConcurrentProbes = 128;

    private static async Task ScanServices(Dictionary<string, DiscoveredConsole> Found, CancellationToken Token)
    {
        try
        {
            using var Gate = new SemaphoreSlim(MaxConcurrentProbes);

            var Probes = EnumerateHosts().Select(async Host =>
            {
                try
                {
                    await Gate.WaitAsync(Token);

                    try
                    {
                        var Console = await ProbeHost(Host.Ip, Host.Local, Token);

                        if (Console != null)
                            Add(Found, Console);
                    }
                    finally
                    {
                        Gate.Release();
                    }
                }
                catch
                {
                }
            });

            await Task.WhenAll(Probes);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Procura GoldHEN, Remote Package Installer e etaHEN em um IP. Null se nenhum responder.
    ///
    /// Faz somente requisições HTTP completas, uma por serviço. Nunca abre uma conexão
    /// vazia: servidores simples de console (como o do GoldHEN) podem tratar uma conexão
    /// sem requisição como um pedido inválido e deixar de atender.
    /// </summary>
    public static async Task<DiscoveredConsole?> ProbeHost(string Ip, string LocalIp, CancellationToken Token = default)
    {
        var Gold = IPHelper.IsGoldHENOnline(Ip);
        var Rpi = IPHelper.IsRPIOnline(Ip);
        var Eta = IPHelper.IsEtaHenOnline(Ip);

        await Task.WhenAll(Gold, Rpi, Eta);

        var Services = new List<string>();
        var Type = "";

        if (Gold.Result)
        {
            Services.Add("GoldHEN");
            Type = "PS4";
        }

        if (Rpi.Result)
            Services.Add("RPI");

        //O etaHEN só existe no PS5
        if (Eta.Result)
        {
            Services.Add("etaHEN");
            Type = "PS5";
        }

        if (Services.Count == 0)
            return null;

        return new DiscoveredConsole
        {
            Ip = Ip,
            LocalIp = LocalIp,
            Type = Type,
            Services = string.Join(" + ", Services)
        };
    }

    /// <summary>Endereços da sub-rede de cada interface (no máximo uma /22), exceto os deste PC.</summary>
    private static List<(string Ip, string Local)> EnumerateHosts()
    {
        var Result = new List<(string, string)>();
        var Seen = new HashSet<string>();
        var Own = new HashSet<string>(IPHelper.EnumLocalIPs());

        void AddRange(IPAddress Address, int Prefix)
        {
            //Sub-redes muito grandes viram a /24 do próprio endereço, para a varredura ser rápida
            if (Prefix < 22)
                Prefix = 24;

            var Mask = Prefix == 0 ? 0u : uint.MaxValue << (32 - Prefix);
            var Bytes = Address.GetAddressBytes();
            var Value = (uint)(Bytes[0] << 24 | Bytes[1] << 16 | Bytes[2] << 8 | Bytes[3]);
            var Network = Value & Mask;
            var Broadcast = Network | ~Mask;

            for (var Host = Network + 1; Host < Broadcast; Host++)
            {
                var Ip = new IPAddress(new[] { (byte)(Host >> 24), (byte)(Host >> 16), (byte)(Host >> 8), (byte)Host }).ToString();

                if (Own.Contains(Ip) || !Seen.Add(Ip))
                    continue;

                Result.Add((Ip, Address.ToString()));
            }
        }

        try
        {
            foreach (var Interface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (Interface.OperationalStatus != OperationalStatus.Up
                    || Interface.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                    continue;

                foreach (var Unicast in Interface.GetIPProperties().UnicastAddresses)
                {
                    if (Unicast.Address.AddressFamily != AddressFamily.InterNetwork || Unicast.IPv4Mask == null)
                        continue;

                    AddRange(Unicast.Address, Unicast.PrefixLength);
                }
            }
        }
        catch
        {
        }

        //Sem informação de máscara (ex.: Android): assume /24 em cada endereço local
        if (Result.Count == 0)
        {
            foreach (var Local in IPHelper.EnumLocalIPs())
            {
                if (IPAddress.TryParse(Local, out var Address))
                    AddRange(Address, 24);
            }
        }

        return Result;
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

                Add(Found, Console);
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
