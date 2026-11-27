using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AutomationPortal.Actions.Windows.Apps;

/// <summary>Sends one line to Desk Pal's control pipe and reads one line back. Tests use a fake so the real pipe is never touched.</summary>
public interface IDeskPalTransport
{
    /// <summary>
    /// The reply line, or null when Desk Pal isn't running (nothing to connect to, or Windows refused the connection because one side
    /// runs as administrator). Throws <see cref="TimeoutException"/> when Desk Pal doesn't answer within <paramref name="readTimeout"/>.
    /// </summary>
    Task<string?> SendAsync(string line, TimeSpan readTimeout, CancellationToken ct);
}

public enum DeskPalLinkState { NotRunning, Running, ControlOff }

/// <summary>What Desk Pal's <c>status</c> command says. Missing fields stay null.</summary>
public sealed record DeskPalStatus(
    string? App, string? Version, string? Edition, bool AllowExternalControl, string? State,
    bool FocusActive, DateTimeOffset? FocusEndsAt, DateTimeOffset? PausedUntil, string? NextNudgeKind, DateTimeOffset? NextNudgeAt,
    int? Stretches, int? EyeBreaks, int? Walks, int? StretchGoal, int? EyeBreakGoal, int? WalkGoal);

/// <summary>A successful reply: the command's result and Desk Pal's fresh status (null when the reply had none).</summary>
public sealed record DeskPalReply(string? Result, DeskPalStatus? Status);

/// <summary>
/// The client side of Desk Pal's control pipe <c>DeskPal-&lt;user SID&gt;</c> (contract 1): <c>{"v":1,"cmd":...,"args":{...}}</c> in,
/// <c>{"ok":true,"result":...,"status":{...}}</c> or <c>{"ok":false,"error":...,"message":...}</c> out. Never starts Desk Pal.
/// </summary>
internal static class DeskPalLink
{
    public static IDeskPalTransport Transport { get; set; } = new NamedPipeDeskPalTransport();

    public static string NotRunning => Strings.Get("DeskPalLink_NotRunning");
    public static string NeedsApproval => Strings.Get("DeskPalLink_NeedsApproval");
    public static string ControlOff => Strings.Get("DeskPalLink_ControlOff");

    /// <summary>Commands that change something wait for Desk Pal's approval card, which stays up for 30 seconds.</summary>
    public static readonly TimeSpan ChangeTimeout = TimeSpan.FromSeconds(35);
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

    public static readonly string[] BreakKinds = { "stretch", "water", "eyes", "walk" };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>One request line, without the line break.</summary>
    public static string BuildRequest(string cmd, IReadOnlyDictionary<string, object>? args = null) =>
        JsonSerializer.Serialize(args is { Count: > 0 }
            ? new Dictionary<string, object> { ["v"] = 1, ["cmd"] = cmd, ["args"] = args }
            : new Dictionary<string, object> { ["v"] = 1, ["cmd"] = cmd }, Json);

    /// <summary>Sends a command and gives back the successful reply. Throws <see cref="ActionException"/> with a plain message otherwise.</summary>
    public static async Task<DeskPalReply> SendAsync(string cmd, IReadOnlyDictionary<string, object>? args, bool changesState, CancellationToken ct)
    {
        string? line;
        try { line = await Transport.SendAsync(BuildRequest(cmd, args), changesState ? ChangeTimeout : ReadTimeout, ct).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            throw new ActionException(changesState
                ? Strings.Get("DeskPalLink_Error_NoAnswerAsk")
                : Strings.Get("DeskPalLink_Error_NoAnswer"));
        }
        catch (IOException) { throw new ActionException(Strings.Get("DeskPalLink_Error_Desk_Pal_Closed_The_Connection")); }
        if (line is null) throw new ActionException(NotRunning);
        return ParseReply(line);
    }

    public static Task<DeskPalReply> StartBreakAsync(string kind, CancellationToken ct)
    {
        var k = kind.Trim().ToLowerInvariant();
        if (!BreakKinds.Contains(k)) throw new ActionException(Strings.Get("DeskPalLink_Error_Choose_A_Kind_Of_Break"));
        return SendAsync("break.start", new Dictionary<string, object> { ["kind"] = k }, true, ct);
    }

    public static Task<DeskPalReply> FocusOnAsync(double minutes, CancellationToken ct) =>
        SendAsync("focus.on", new Dictionary<string, object> { ["minutes"] = Minutes(minutes, 5, 120) }, true, ct);

    public static Task<DeskPalReply> PauseAsync(double minutes, CancellationToken ct) =>
        SendAsync("pause", new Dictionary<string, object> { ["minutes"] = Minutes(minutes, 5, 480) }, true, ct);

    /// <summary>Rounded and kept in Desk Pal's range, as Desk Pal itself would.</summary>
    public static int Minutes(double value, int min, int max) =>
        double.IsNaN(value) ? min : (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), min, max);

    public static async Task<DeskPalStatus> GetStatusAsync(CancellationToken ct) =>
        (await SendAsync("status", null, false, ct).ConfigureAwait(false)).Status
        ?? throw new ActionException(Strings.Get("DeskPalLink_Error_Desk_Pal_Sent_A_Reply"));

    public static DeskPalReply ParseReply(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) throw new ActionException(Strings.Get("DeskPalLink_Error_Desk_Pal_Sent_A_Reply"));
            if (r.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var status = r.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Object ? ParseStatus(s) : null;
                return new DeskPalReply(Str(r, "result"), status);
            }
            throw new ActionException(ErrorMessage(Str(r, "error"), Str(r, "message")));
        }
        catch (JsonException) { throw new ActionException(Strings.Get("DeskPalLink_Error_Desk_Pal_Sent_A_Reply")); }
    }

    /// <summary>Plain words for Desk Pal's error code. Desk Pal's own message is used where it explains the problem best.</summary>
    public static string ErrorMessage(string? error, string? message)
    {
        var theirs = Clean(message);
        return error switch
        {
            "needs-approval" => NeedsApproval,
            "denied" => Strings.Get("DeskPalLink_Error_Denied"),
            "not-allowed" => theirs ?? ControlOff,
            "busy" => theirs ?? Strings.Get("DeskPalLink_Error_Busy"),
            "rate-limited" => theirs ?? Strings.Get("DeskPalLink_Error_RateLimited"),
            "unsupported-version" or "unknown-command" => Strings.Get("DeskPalLink_Error_UnsupportedVersion"),
            "bad-args" => theirs ?? Strings.Get("DeskPalLink_Error_BadArgs"),
            "timeout" => Strings.Get("DeskPalLink_Error_Timeout"),
            _ => theirs ?? Strings.Get("DeskPalLink_Error_Generic"),
        };
    }

    /// <summary>A short, single-line message, or null when there is nothing usable.</summary>
    private static string? Clean(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var s = new string(message.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (s.Length == 0) return null;
        return s.Length <= 300 ? s : s[..300] + "…";
    }

    public static DeskPalStatus ParseStatus(JsonElement s)
    {
        JsonElement focus = default, nudge = default, today = default, goals = default;
        var hasFocus = s.TryGetProperty("focus", out focus) && focus.ValueKind == JsonValueKind.Object;
        var hasNudge = s.TryGetProperty("nextNudge", out nudge) && nudge.ValueKind == JsonValueKind.Object;
        var hasToday = s.TryGetProperty("today", out today) && today.ValueKind == JsonValueKind.Object;
        var hasGoals = s.TryGetProperty("goals", out goals) && goals.ValueKind == JsonValueKind.Object;
        return new DeskPalStatus(
            Str(s, "app"), Str(s, "version"), Str(s, "edition"), Bool(s, "allowExternalControl"), Str(s, "state"),
            hasFocus && Bool(focus, "active"), hasFocus ? Time(focus, "endsAt") : null, Time(s, "pausedUntil"),
            hasNudge ? Str(nudge, "kind") : null, hasNudge ? Time(nudge, "dueAt") : null,
            hasToday ? Int(today, "stretches") : null, hasToday ? Int(today, "eyeBreaks") : null, hasToday ? Int(today, "walks") : null,
            hasGoals ? Int(goals, "stretches") : null, hasGoals ? Int(goals, "eyeBreaks") : null, hasGoals ? Int(goals, "walks") : null);
    }

    /// <summary>Asks Desk Pal's read-only status. A missing, slow or unreadable reply, or one from something else, counts as not running.</summary>
    public static async Task<DeskPalLinkState> ProbeAsync(CancellationToken ct)
    {
        try
        {
            var status = await GetStatusAsync(ct).ConfigureAwait(false);
            if (!string.Equals(status.App, "DeskPal", StringComparison.Ordinal)) return DeskPalLinkState.NotRunning;
            return status.AllowExternalControl ? DeskPalLinkState.Running : DeskPalLinkState.ControlOff;
        }
        catch (ActionException) { return DeskPalLinkState.NotRunning; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        Str(e, name) is { } t && DateTimeOffset.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
}

internal sealed class NamedPipeDeskPalTransport : IDeskPalTransport
{
    private const int ConnectTimeoutMs = 1000;

    private readonly string? _pipeName;

    /// <param name="pipeName">Tests only: another pipe name. Null means Desk Pal's real one.</param>
    public NamedPipeDeskPalTransport(string? pipeName = null) { _pipeName = pipeName; }

    /// <summary>The plain pipe name (a Desk Pal debug run adds a suffix; clients use the plain name).</summary>
    public static string DefaultPipeName => "DeskPal-" + InstanceNames.UserSid;

    public async Task<string?> SendAsync(string line, TimeSpan readTimeout, CancellationToken ct)
    {
        // CurrentUserOnly: the server must belong to this user. Windows refuses the connection when only one side runs as administrator.
        await using var pipe = new NamedPipeClientStream(".", _pipeName ?? DefaultPipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(readTimeout);
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(line + "\n");
            await pipe.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            return await reader.ReadLineAsync(cts.Token).ConfigureAwait(false)
                ?? throw new IOException(Strings.Get("DeskPalLink_Error_ClosedEarly"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(); }
    }
}
