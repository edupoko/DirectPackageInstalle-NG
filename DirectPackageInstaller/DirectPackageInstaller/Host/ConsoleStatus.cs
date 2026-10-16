using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using DirectPackageInstaller.Tasks;

namespace DirectPackageInstaller.Host;

public enum ConsoleState
{
    /// <summary>Nenhum IP de console configurado.</summary>
    None,
    Checking,
    /// <summary>Pelo menos um instalador (GoldHEN, RPI ou etaHEN) respondeu.</summary>
    Ready,
    /// <summary>O console responde, mas nenhum instalador foi encontrado.</summary>
    NoInstaller,
    /// <summary>O console não responde.</summary>
    Offline
}

public sealed class ConsoleStatusResult
{
    public ConsoleState State { get; init; }
    public string Ip { get; init; } = "";

    /// <summary>Instaladores encontrados, ex.: "GoldHEN" ou "GoldHEN + RPI".</summary>
    public string Services { get; init; } = "";
}

/// <summary>Verifica se o console configurado está acessível e pronto para receber um PKG.</summary>
public static class ConsoleStatusChecker
{
    public static async Task<ConsoleStatusResult> CheckAsync(string? Ip)
    {
        if (string.IsNullOrWhiteSpace(Ip) || Ip == "0.0.0.0" || !IPAddress.TryParse(Ip, out _))
            return new ConsoleStatusResult { State = ConsoleState.None };

        var Gold = IPHelper.IsGoldHENOnline(Ip);
        var Rpi = IPHelper.IsRPIOnline(Ip);
        var Eta = IPHelper.IsEtaHenOnline(Ip);

        await Task.WhenAll(Gold, Rpi, Eta);

        var Services = new List<string>();

        //O payload do DPI, quando já está residente no console, também conta como pronto
        if (Gold.Result || Installer.Payload.ClientRunning)
            Services.Add("GoldHEN");

        if (Rpi.Result)
            Services.Add("RPI");

        if (Eta.Result)
            Services.Add("etaHEN");

        if (Services.Count > 0)
            return new ConsoleStatusResult { State = ConsoleState.Ready, Ip = Ip, Services = string.Join(" + ", Services) };

        return new ConsoleStatusResult
        {
            State = await Reachable(Ip) ? ConsoleState.NoInstaller : ConsoleState.Offline,
            Ip = Ip
        };
    }

    private static async Task<bool> Reachable(string Ip)
    {
        try
        {
            using var Ping = new Ping();
            var Reply = await Ping.SendPingAsync(Ip, 1000);
            return Reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
