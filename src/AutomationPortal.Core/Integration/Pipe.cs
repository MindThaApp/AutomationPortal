using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>Process exit codes of <c>ap</c> (see docs/CLI.md).</summary>
public static class CliExit
{
    public const int Ok = 0;
    /// <summary>The shortcut ran but a step failed.</summary>
    public const int Failed = 1;
    /// <summary>Wrong or missing arguments.</summary>
    public const int Usage = 2;
    public const int NotFound = 3;
    /// <summary>The shortcut was cancelled or stopped before finishing.</summary>
    public const int Cancelled = 4;
    /// <summary>A file, import or export problem, or the app couldn't do it here.</summary>
    public const int Problem = 5;
    /// <summary>Another app asked to run a shortcut and the user said no, nobody answered in time, or that app already had a request waiting.</summary>
    public const int Declined = 6;
}

/// <summary>One request on the Automation Portal pipe. Kind: ping, capabilities, list, run, import, event (contract v1, see docs/CLI.md), and the internal launch and export.</summary>
public sealed class PipeRequest
{
    public string Kind { get; set; } = "";
    /// <summary>launch: the command line of the second AutomationPortal.exe.</summary>
    public string[] Args { get; set; } = Array.Empty<string>();
    public string? Name { get; set; }
    /// <summary>run: input text. import: the .autoportal file text. event: the details, a JSON object as text.</summary>
    public string? Input { get; set; }
    public string[] Files { get; set; } = Array.Empty<string>();
    public bool Wait { get; set; }
    public bool Json { get; set; }
    /// <summary>Who says they sent it ("cli" from ap.exe). Advisory only: any program running as the same user can set it.</summary>
    public string? Source { get; set; }
    /// <summary>run: ask the user first, naming <see cref="Source"/> (a caller id, see <see cref="Callers"/>) as the app that wants to run it.</summary>
    public bool Ask { get; set; }
    /// <summary>run, preview (contract 1.1): who says they are asking, shown on the request sheet. Kind "ai" is always asked.</summary>
    public CallerInfo? Caller { get; set; }
}

/// <summary>
/// The app that says it is asking (<c>{"app":"AI Portal","kind":"ai"}</c>). Advisory, like <see cref="PipeRequest.Source"/>: it is
/// shown to the user and only ever makes Automation Portal ask more, never less.
/// </summary>
public sealed class CallerInfo
{
    public const int MaxAppLength = 48;

    public string? App { get; set; }
    /// <summary>"app" (the default) or "ai" for an assistant acting for the user.</summary>
    public string? Kind { get; set; }

    public bool IsAi => string.Equals(Kind?.Trim(), "ai", StringComparison.OrdinalIgnoreCase);

    /// <summary>The app's name as it may be shown: control characters dropped, spaces collapsed, at most 48 characters; null when empty.</summary>
    public string? SafeAppName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(App)) return null;
            var sb = new StringBuilder();
            foreach (var c in App)
            {
                if (char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format) continue;
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
                else sb.Append(c);
            }
            var text = sb.ToString().Trim();
            if (text.Length > MaxAppLength) text = text[..MaxAppLength].TrimEnd() + "…";
            return text.Length == 0 ? null : text;
        }
    }
}

public sealed class PipeResponse
{
    public int ExitCode { get; set; }
    /// <summary>What the command prints to standard output.</summary>
    public string? Output { get; set; }
    /// <summary>What the command prints to standard error (a plain-English problem).</summary>
    public string? Message { get; set; }

    public static PipeResponse Done(string? output = null, string? message = null) => new() { ExitCode = CliExit.Ok, Output = output, Message = message };
    public static PipeResponse Error(int code, string message) => new() { ExitCode = code, Message = message };
}

internal static class PipeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static readonly UTF8Encoding Utf8 = new(false);
}

/// <summary>
/// Listens on the per-user Automation Portal pipe. One request line in, one response line out. Closing the pipe early cancels the request.
/// A request line longer than <see cref="MaxRequestBytes"/>, or one that doesn't arrive within <see cref="ReadTimeout"/>, gets an error reply and the pipe is closed.
/// </summary>
public sealed class PipeServer : IDisposable
{
    public const int DefaultMaxRequestBytes = 4 * 1024 * 1024;
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The longest request line accepted, in UTF-8 bytes, without the line break.</summary>
    public int MaxRequestBytes { get; init; } = DefaultMaxRequestBytes;
    /// <summary>How long a client has to send its whole request line after connecting. Also bounds sending an error reply.</summary>
    public TimeSpan ReadTimeout { get; init; } = DefaultReadTimeout;

    private readonly string _name;
    private readonly Func<PipeRequest, CancellationToken, Task<PipeResponse>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public PipeServer(string name, Func<PipeRequest, CancellationToken, Task<PipeResponse>> handler)
    {
        _name = name;
        _handler = handler;
    }

    /// <summary>Creates the pipe right away (so a client that connects after this returns finds it) and starts accepting.</summary>
    public void Start()
    {
        var first = Create();
        _loop = Task.Run(() => AcceptLoopAsync(first));
    }

    private NamedPipeServerStream Create() =>
        new(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptLoopAsync(NamedPipeServerStream pipe)
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { pipe.Dispose(); return; }
            catch (IOException) { pipe.Dispose(); pipe = Create(); continue; }

            var connected = pipe;
            try { pipe = Create(); }
            catch (IOException) { connected.Dispose(); return; }
            _ = Task.Run(() => ServeAsync(connected));
        }
        pipe.Dispose();
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        await using var owned = pipe;
        try
        {
            var (line, problem) = await ReadRequestLineAsync(pipe).ConfigureAwait(false);
            if (problem is not null)
            {
                await ReplyAndCloseAsync(pipe, problem).ConfigureAwait(false);
                return;
            }
            if (line is null) return;

            PipeRequest? request = null;
            try { request = JsonSerializer.Deserialize<PipeRequest>(line, PipeJson.Options); } catch (JsonException) { }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            using var watchStop = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try { if (await pipe.ReadAsync(new byte[1], watchStop.Token).ConfigureAwait(false) == 0) cts.Cancel(); }
                catch { }
            });

            PipeResponse response;
            if (request is null) response = PipeResponse.Error(CliExit.Usage, "Automation Portal couldn't understand that request.");
            else
            {
                try { response = await _handler(request, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { response = PipeResponse.Error(CliExit.Cancelled, "Cancelled."); }
                catch (Exception ex) { response = PipeResponse.Error(CliExit.Problem, "Something went wrong. " + ex.Message); }
            }
            watchStop.Cancel();

            if (cts.IsCancellationRequested && !_stop.IsCancellationRequested) return;
            var bytes = PipeJson.Utf8.GetBytes(JsonSerializer.Serialize(response, PipeJson.Options) + "\n");
            await pipe.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
            await pipe.FlushAsync(_stop.Token).ConfigureAwait(false);
            if (OperatingSystem.IsWindows()) { try { pipe.WaitForPipeDrain(); } catch (IOException) { } }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    /// <summary>Reads up to the first line break. Null line when the client left without sending anything; a problem when the line is too long or too slow.</summary>
    private async Task<(string? Line, PipeResponse? Problem)> ReadRequestLineAsync(Stream pipe)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(ReadTimeout);
        var buffer = new byte[16 * 1024];
        using var line = new MemoryStream();
        try
        {
            while (true)
            {
                var n = await pipe.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (n == 0)
                {
                    if (line.Length == 0) return (null, null);
                    break;
                }
                var end = Array.IndexOf(buffer, (byte)'\n', 0, n);
                var take = end < 0 ? n : end;
                if (line.Length + take > MaxRequestBytes)
                    return (null, PipeResponse.Error(CliExit.Problem, $"That request is too large. Automation Portal accepts up to {MaxRequestBytes / (1024 * 1024)} MB at a time."));
                line.Write(buffer, 0, take);
                if (end >= 0) break;
            }
        }
        catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
        {
            return (null, PipeResponse.Error(CliExit.Usage, "Automation Portal didn't receive a complete request in time."));
        }

        var text = PipeJson.Utf8.GetString(line.GetBuffer(), 0, (int)line.Length);
        return (text.EndsWith('\r') ? text[..^1] : text, null);
    }

    /// <summary>Best effort: the client may not be reading (it never sent anything, or it is still busy sending too much), so this never waits long.</summary>
    private async Task ReplyAndCloseAsync(Stream pipe, PipeResponse response)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(ReadTimeout);
        var bytes = PipeJson.Utf8.GetBytes(JsonSerializer.Serialize(response, PipeJson.Options) + "\n");
        await pipe.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(500); } catch { }
        _stop.Dispose();
    }
}

public static class PipeClient
{
    /// <summary>Sends one request to the running app. Null when no app is listening (nothing to connect to within <paramref name="connectTimeout"/>).</summary>
    public static async Task<PipeResponse?> TrySendAsync(string pipeName, PipeRequest request, TimeSpan connectTimeout, CancellationToken ct = default)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await client.ConnectAsync(Math.Max(1, (int)connectTimeout.TotalMilliseconds), ct).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }

        var bytes = PipeJson.Utf8.GetBytes(JsonSerializer.Serialize(request, PipeJson.Options) + "\n");
        // The app stops reading at the limit, which would leave this side stuck mid-write and never seeing its answer.
        if (bytes.Length - 1 > PipeServer.DefaultMaxRequestBytes)
            return PipeResponse.Error(CliExit.Problem, $"That is too large to send to Automation Portal. It accepts up to {PipeServer.DefaultMaxRequestBytes / (1024 * 1024)} MB at a time.");
        await client.WriteAsync(bytes, ct).ConfigureAwait(false);
        await client.FlushAsync(ct).ConfigureAwait(false);

        using var reader = new StreamReader(client, PipeJson.Utf8, false, 4096, leaveOpen: true);
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (line is null) throw new IOException("Automation Portal closed the connection before it answered.");
        return JsonSerializer.Deserialize<PipeResponse>(line, PipeJson.Options) ?? throw new IOException("Automation Portal sent an answer that couldn't be read.");
    }
}
