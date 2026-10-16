using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DirectPackageInstaller.Host;

/// <summary>Resposta de um comando do Remote Package Installer.</summary>
public sealed class RpiResponse
{
    public bool Success { get; init; }

    /// <summary>Mensagem de erro legível (vazia se deu certo).</summary>
    public string Error { get; init; } = "";

    /// <summary>Código de erro do sistema, quando o console devolve um (ex.: 0x80990085).</summary>
    public uint ErrorCode { get; init; }

    public Dictionary<string, string> Fields { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public long GetNumber(string Key)
    {
        if (!Fields.TryGetValue(Key, out var Text))
            return 0;

        //O RPI escreve números em hexadecimal sem aspas (0x1A2B), o que não é JSON válido
        if (Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(Text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var Hex) ? Hex : 0;

        return long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var Dec) ? Dec : 0;
    }
}

/// <summary>Progresso de uma tarefa de download/instalação, como informado pelo console.</summary>
public sealed class RpiProgress
{
    public long Length { get; init; }
    public long Transferred { get; init; }
    public long LengthTotal { get; init; }
    public long TransferredTotal { get; init; }
    public int RestSecTotal { get; init; }
    public int NumIndex { get; init; }
    public int NumTotal { get; init; }
    public int PreparingPercent { get; init; }
    public int LocalCopyPercent { get; init; }

    /// <summary>Erro da tarefa (0 = sem erro).</summary>
    public uint Error { get; init; }

    public bool IsComplete => LengthTotal > 0 && TransferredTotal >= LengthTotal;
}

/// <summary>
/// Cliente da API HTTP do Remote Package Installer (porta 12800, POST com JSON).
/// Protocolo conferido no código-fonte do servidor (flatz/ps4_remote_pkg_installer).
/// </summary>
public static class RpiClient
{
    /// <summary>Porta do servidor do RPI. Só muda nos testes.</summary>
    public static int Port = 12800;

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly Regex Pair = new Regex(
        "\"(?<k>[A-Za-z_]+)\"\\s*:\\s*(?:\"(?<s>(?:[^\"\\\\]|\\\\.)*)\"|(?<n>[^\\s,}]+))",
        RegexOptions.Compiled);

    //Title ID: 4 letras + 5 dígitos. Content ID: letras, dígitos, hífen e sublinhado.
    private static readonly Regex TitleIdFormat = new Regex("^[A-Z]{4}[0-9]{5}$", RegexOptions.Compiled);
    private static readonly Regex ContentIdFormat = new Regex("^[A-Za-z0-9_\\-]{1,64}$", RegexOptions.Compiled);

    public static bool IsValidTitleId(string? Id) => Id != null && TitleIdFormat.IsMatch(Id);

    public static bool IsValidContentId(string? Id) => Id != null && ContentIdFormat.IsMatch(Id);

    /// <summary>Lê a resposta, tolerando os números hexadecimais sem aspas.</summary>
    public static RpiResponse Parse(string Body)
    {
        var Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match Match in Pair.Matches(Body ?? ""))
            Fields[Match.Groups["k"].Value] = Match.Groups["s"].Success ? Match.Groups["s"].Value : Match.Groups["n"].Value;

        var Success = Fields.TryGetValue("status", out var Status) && Status == "success";

        uint Code = 0;
        var Error = "";

        if (!Success)
        {
            if (Fields.TryGetValue("error", out var Message))
                Error = Message;

            if (Fields.TryGetValue("error_code", out var CodeText))
            {
                var Digits = CodeText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? CodeText.Substring(2) : CodeText;
                uint.TryParse(Digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out Code);
                Error = DescribeError(Code);
            }

            if (Error.Length == 0)
                Error = "Unknown error";
        }

        return new RpiResponse { Success = Success, Error = Error, ErrorCode = Code, Fields = Fields };
    }

    public static string DescribeError(uint Code) => Code switch
    {
        0x80990085 => "Not enough free space on the console (0x80990085)",
        0x80990088 => "This package is already registered on the console (0x80990088)",
        0x80990086 => "Remove old downloads from the console's notification list (0x80990086)",
        _ => $"Console error 0x{Code:X8}"
    };

    public static async Task<RpiResponse> CallAsync(string Ip, string Endpoint, string Json)
    {
        try
        {
            using var Content = new StringContent(Json, Encoding.UTF8);
            using var Reply = await Http.PostAsync($"http://{Ip}:{Port}/api/{Endpoint}", Content);

            return Parse(await Reply.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            return new RpiResponse { Success = false, Error = ex is TaskCanceledException ? "The console didn't answer in time" : ex.Message };
        }
    }

    // ---- Tarefas ----

    public static async Task<RpiProgress?> GetTaskProgress(string Ip, int TaskId)
    {
        var Reply = await CallAsync(Ip, "get_task_progress", $"{{\"task_id\":{TaskId}}}");

        if (!Reply.Success)
            return null;

        return new RpiProgress
        {
            Length = Reply.GetNumber("length"),
            Transferred = Reply.GetNumber("transferred"),
            LengthTotal = Reply.GetNumber("length_total"),
            TransferredTotal = Reply.GetNumber("transferred_total"),
            RestSecTotal = (int)Reply.GetNumber("rest_sec_total"),
            NumIndex = (int)Reply.GetNumber("num_index"),
            NumTotal = (int)Reply.GetNumber("num_total"),
            PreparingPercent = (int)Reply.GetNumber("preparing_percent"),
            LocalCopyPercent = (int)Reply.GetNumber("local_copy_percent"),
            Error = (uint)Reply.GetNumber("error")
        };
    }

    public static Task<RpiResponse> PauseTask(string Ip, int TaskId) => CallAsync(Ip, "pause_task", $"{{\"task_id\":{TaskId}}}");

    public static Task<RpiResponse> ResumeTask(string Ip, int TaskId) => CallAsync(Ip, "resume_task", $"{{\"task_id\":{TaskId}}}");

    public static Task<RpiResponse> StopTask(string Ip, int TaskId) => CallAsync(Ip, "stop_task", $"{{\"task_id\":{TaskId}}}");

    public static Task<RpiResponse> UnregisterTask(string Ip, int TaskId) => CallAsync(Ip, "unregister_task", $"{{\"task_id\":{TaskId}}}");

    // ---- Instalado / desinstalar ----

    /// <summary>Pergunta ao console se o jogo está instalado. O tamanho vem junto quando existe.</summary>
    public static async Task<(bool Ok, bool Exists, long Size)> IsInstalled(string Ip, string TitleId)
    {
        if (!IsValidTitleId(TitleId))
            return (false, false, 0);

        var Reply = await CallAsync(Ip, "is_exists", $"{{\"title_id\":\"{TitleId}\"}}");

        if (!Reply.Success)
            return (false, false, 0);

        var Exists = Reply.Fields.TryGetValue("exists", out var Text) && Text == "true";

        return (true, Exists, Exists ? Reply.GetNumber("size") : 0);
    }

    public static Task<RpiResponse> UninstallGame(string Ip, string TitleId) =>
        IsValidTitleId(TitleId) ? CallAsync(Ip, "uninstall_game", $"{{\"title_id\":\"{TitleId}\"}}") : Invalid("title ID");

    public static Task<RpiResponse> UninstallPatch(string Ip, string TitleId) =>
        IsValidTitleId(TitleId) ? CallAsync(Ip, "uninstall_patch", $"{{\"title_id\":\"{TitleId}\"}}") : Invalid("title ID");

    public static Task<RpiResponse> UninstallAdditionalContent(string Ip, string ContentId) =>
        IsValidContentId(ContentId) ? CallAsync(Ip, "uninstall_ac", $"{{\"content_id\":\"{ContentId}\"}}") : Invalid("content ID");

    public static Task<RpiResponse> UninstallTheme(string Ip, string ContentId) =>
        IsValidContentId(ContentId) ? CallAsync(Ip, "uninstall_theme", $"{{\"content_id\":\"{ContentId}\"}}") : Invalid("content ID");

    private static Task<RpiResponse> Invalid(string What) =>
        Task.FromResult(new RpiResponse { Success = false, Error = $"Invalid {What}" });
}
